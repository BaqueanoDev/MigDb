using Dapper;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration.Generate;
using MigDb.Core.Options;
using MigDb.Core.Schema;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace MigDb.Core.Migration.Revert;

public sealed class MigrationJournalReverter(
    ILogger<MigrationJournalReverter> logger,
    SQLConnectionFactory sqlFactory,
    SchemaRepository schemaRepo,
    MigrationRunRepository runRepo,
    MigrationDirectoryRepository directoryRepo,
    MigrationFileRepository fileRepo,
    MigrationProgrammableRepository migrationProgrammableRepo,
    SchemaProgrammableRepository schemaProgrammableRepo,
    MigrationRevertRepository revertRepo,
    DatabaseOptions dbOptions)
{
    public async Task<MigrationRevertPlan> RevertAsync(
        IReadOnlyList<MigrationScript> scripts,
        IReadOnlyList<string> directoryNames,
        MigrationSource source,
        bool commit = false,
        string? targetRevision = null,
        CancellationToken ct = default)
    {
        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(null, ct);
        await using DbTransaction transaction = await scope.Connection.BeginTransactionAsync(ct);

        if (!await schemaRepo.MigrationSchemaInitialisedAsync(transaction, ct))
        {
            logger.LogError("Migration schema/tables are missing in the target database");
            throw new MigrationSchemaNotInitialisedException();
        }

        int databaseLockResult = await schemaRepo.AcquireMigrationLockAsync(transaction, ct);

        if (databaseLockResult < 0)
        {
            logger.LogError("Could not acquire migration lock (result {Result}) - another migration may be in progress", databaseLockResult);
            throw new MigrationAppLockException(databaseLockResult);
        }

        MigrationRevertPlan plan = await BuildRevertPlanAsync(directoryNames, source, transaction, ct);

        if (!commit)
        {
            logger.LogDebug("Preview only - rolling back revert");

            await transaction.RollbackAsync(ct);

            return plan;
        }

        // schema first, so the journal is only wound back over changes that actually landed
        await ExecuteScriptsAsync(scripts, scope, transaction, ct);

        await ApplyRevertPlanAsync(plan, targetRevision, transaction, ct);
        await transaction.CommitAsync(ct);

        return plan;
    }

    /// <summary>
    /// Runs the generated revert scripts against the target, split on batch separators since a
    /// generated table rebuild can carry GO
    /// </summary>
    private async Task ExecuteScriptsAsync(IReadOnlyList<MigrationScript> scripts, SQLConnectionLease scope, IDbTransaction transaction, CancellationToken ct)
    {
        foreach (MigrationScript script in scripts)
        {
            logger.LogInformation("Applying revert script for {Object}", script.Object.FullName);

            foreach (string batch in DacFxUtils.SplitScriptByBatch(script.Script))
            {
                CommandDefinition command = new(batch, transaction: transaction, commandTimeout: dbOptions.CommandTimeoutSeconds, cancellationToken: ct);

                await scope.Connection.ExecuteAsync(command);
            }
        }

        logger.LogInformation("Executed {Count} revert script(s)", scripts.Count);
    }

    private async Task<MigrationRevertPlan> BuildRevertPlanAsync(IReadOnlyList<string> directoryNames, MigrationSource source, IDbTransaction transaction, CancellationToken ct)
    {
        if (directoryNames.Count == 0)
            return MigrationRevertPlan.Empty;

        MigrationSourceType sourceType = MigrationSource.ToMigrationSourceType(source);
        string? sourceName = MigrationSource.ToSourceName(source);

        IReadOnlyList<MigrationDirectoryEntity> journalled = await directoryRepo.GetByNameListAsync(directoryNames, transaction, ct);

        List<MigrationDirectoryEntity> directories = [.. journalled
            .Where(d => d.Source == sourceType && string.Equals(d.SourceName, sourceName, StringComparison.OrdinalIgnoreCase))];

        if (directories.Count == 0)
        {
            logger.LogDebug("None of the {Count} reverted directory name(s) are journalled for {Source}", directoryNames.Count, sourceType);

            return MigrationRevertPlan.Empty;
        }

        long[] directoryIds = [.. directories.Select(d => d.MigrationDirectoryId)];
        HashSet<long> droppedDirectoryIds = [.. directoryIds];

        IReadOnlyList<MigrationFileEntity> files = await fileRepo.GetByDirectoryIdListAsync(directoryIds, transaction, ct);

        List<long> emptiedRunIds = [];

        foreach (long runId in directories.Select(d => d.MigrationRunId).Distinct())
        {
            IReadOnlyList<MigrationDirectoryEntity> runDirectories = await directoryRepo.GetByRunIdAsync(runId, transaction, ct);

            if (runDirectories.All(d => droppedDirectoryIds.Contains(d.MigrationDirectoryId)))
            {
                emptiedRunIds.Add(runId);
                continue;
            }

            logger.LogWarning("Run {RunId} also applied directories outside this revert, leaving the run and its programmables in place", runId);
        }

        IReadOnlyList<MigrationRunEntity> runs = await runRepo.GetByIdListAsync(emptiedRunIds, transaction, ct);

        List<MigrationProgrammableEntity> programmableEntries = [];

        foreach (long runId in emptiedRunIds)
            programmableEntries.AddRange(await migrationProgrammableRepo.GetByRunIdAsync(runId, transaction, ct));

        long[] schemaProgrammableIds = [.. programmableEntries.Select(p => p.SchemaProgrammableId).Distinct()];

        IReadOnlyList<SchemaProgrammableEntity> programmables = await schemaProgrammableRepo.GetByIdListAsync(schemaProgrammableIds, transaction, ct);

        ILookup<long, MigrationProgrammableEntity> entriesByProgrammable = programmableEntries.ToLookup(p => p.SchemaProgrammableId);

        List<ProgrammableRewind> rewinds = [];

        foreach (SchemaProgrammableEntity programmable in programmables)
        {
            List<MigrationProgrammableEntity> dropped = [.. entriesByProgrammable[programmable.SchemaProgrammableId]
                .OrderBy(p => p.MigrationProgrammableId)];

            if (!programmable.Hash.SequenceEqual(dropped[^1].HashAfter))
            {
                logger.LogWarning("Programmable {Object} has moved on since run {RunId} deployed it, leaving it in place", programmable.FullName, dropped[^1].MigrationRunId);
                continue;
            }

            byte[]? previousHash = dropped[0].HashBefore;

            if (previousHash is null)
            {
                IReadOnlyList<MigrationProgrammableEntity> history = await migrationProgrammableRepo.GetBySchemaProgrammableIdAsync(programmable.SchemaProgrammableId, transaction, ct);

                if (history.Count != dropped.Count)
                {
                    logger.LogWarning("Programmable {Object} still has deploys outside this revert, leaving it in place", programmable.FullName);
                    continue;
                }
            }

            rewinds.Add(new ProgrammableRewind(programmable, previousHash));
        }

        logger.LogDebug("Journal revert covers {Directories} directory(s), {Files} file(s), {Runs} run(s) and {Programmables} programmable(s)",
            directories.Count, files.Count, runs.Count, rewinds.Count);

        return new MigrationRevertPlan(directories, files, runs, programmableEntries, rewinds);
    }

    private async Task ApplyRevertPlanAsync(MigrationRevertPlan plan, string? targetRevision, IDbTransaction transaction, CancellationToken ct)
    {
        await RecordRevertAsync(plan, targetRevision, transaction, ct);

        foreach (MigrationFileEntity file in plan.Files)
            await fileRepo.DeleteAsync(file.MigrationFileId, transaction, ct);

        foreach (MigrationDirectoryEntity directory in plan.Directories)
            await directoryRepo.DeleteAsync(directory.MigrationDirectoryId, transaction, ct);

        foreach (MigrationProgrammableEntity entry in plan.ProgrammableEntries)
            await migrationProgrammableRepo.DeleteAsync(entry.MigrationProgrammableId, transaction, ct);

        foreach (ProgrammableRewind rewind in plan.Programmables)
        {
            if (rewind.PreviousHash is null)
            {
                await schemaProgrammableRepo.DeleteAsync(rewind.Programmable.SchemaProgrammableId, transaction, ct);
                continue;
            }

            rewind.Programmable.Hash = rewind.PreviousHash;

            await schemaProgrammableRepo.UpdateAsync(rewind.Programmable, transaction, ct);
        }

        foreach (MigrationRunEntity run in plan.Runs)
            await runRepo.DeleteAsync(run.MigrationRunId, transaction, ct);

        logger.LogInformation("Reverted {Directories} directory(s) and {Runs} run(s) out of the journal", plan.Directories.Count, plan.Runs.Count);
    }

    private async Task RecordRevertAsync(MigrationRevertPlan plan, string? targetRevision, IDbTransaction transaction, CancellationToken ct)
    {
        DateTime revertedAt = DateTime.UtcNow;
        string revertedBy = $"{Environment.UserName}@{Environment.MachineName}";

        Dictionary<long, MigrationRunEntity> runs = plan.Runs.ToDictionary(r => r.MigrationRunId);

        ILookup<long, MigrationFileEntity> filesByDirectory = plan.Files.ToLookup(f => f.MigrationDirectoryId);

        foreach (MigrationDirectoryEntity directory in plan.Directories)
        {
            bool runDropped = runs.TryGetValue(directory.MigrationRunId, out MigrationRunEntity? run);

            string files = string.Join(", ", filesByDirectory[directory.MigrationDirectoryId]
                .OrderBy(f => f.Sequence)
                .Select(f => f.Name));

            MigrationRevertEntity entry = new()
            {
                RevertedAt = revertedAt,
                RevertedBy = revertedBy,
                Kind = MigrationRevertKind.Directory,
                Name = directory.Name,
                Source = directory.Source,
                SourceName = directory.SourceName,
                Hash = directory.Hash,
                MigrationRunId = runDropped ? directory.MigrationRunId : null,
                AppliedAt = run?.StartTime,
                TargetRevision = targetRevision,
                Detail = files.Length == 0 ? null : files,
            };

            await revertRepo.InsertAsync(entry, transaction, ct);
        }

        MigrationSourceType sourceType = plan.Directories.Count == 0
            ? MigrationSourceType.Common
            : plan.Directories[0].Source;

        foreach (ProgrammableRewind rewind in plan.Programmables)
        {
            MigrationRevertEntity entry = new()
            {
                RevertedAt = revertedAt,
                RevertedBy = revertedBy,
                Kind = MigrationRevertKind.Programmable,
                Name = rewind.Programmable.FullName,
                Source = sourceType,
                SourceName = rewind.Programmable.SourceName,
                Hash = rewind.Programmable.Hash,
                PreviousHash = rewind.PreviousHash,
                TargetRevision = targetRevision,
                Detail = rewind.IsDrop ? "Journal row dropped, no surviving deploy" : null,
            };

            await revertRepo.InsertAsync(entry, transaction, ct);
        }
    }
}
