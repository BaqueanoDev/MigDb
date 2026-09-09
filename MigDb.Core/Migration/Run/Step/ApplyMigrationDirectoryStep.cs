using Dapper;
using MigDb.Core.Migration;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration.Manifest;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Options;
using MigDb.Core.Schema;
using MigDb.Core.Utils.ScriptDom;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;

namespace MigDb.Core.Migration.Run.Step;

public sealed class ApplyMigrationDirectoryStep(
    IReadOnlyList<MigrationDirectory> migrations,
    MigrationRunMode mode,
    ILogger<ApplyMigrationDirectoryStep> logger,
    MigrationHasher hasher,
    MigrationDirectoryRepository directoryRepo,
    MigrationFileRepository fileRepo,
    DatabaseOptions dbOptions,
    SQLConnectionFactory sqlFactory) : IMigrationStep
{
    public async Task<MigrationStepResult> ExecuteAsync(MigrationRunEntity run, IDbTransaction transaction, CancellationToken ct)
    {
        List<MigrationDirectory> newMigrations = new(migrations.Count);

        foreach (MigrationDirectory m in migrations)
        {
            MigrationDirectoryEntity? existingRecord = await directoryRepo.GetByNameAsync(m.Name, m.SourceType, transaction, ct);

            if (existingRecord is not null)
            {
                logger.LogInformation("Directory {Directory} already applied (run: {RunId}) - skipping", m.Name, existingRecord.MigrationRunId);
                continue;
            }

            newMigrations.Add(m);
        }

        if (newMigrations.Count == 0)
        {
            logger.LogInformation("Nothing to apply, all migrations already applied");
            return MigrationStepResult.Empty;
        }

        int runDuration = 0;
        int migrationSequence = 0;

        List<SchemaObject> objectsChanged = [];
        HashSet<SchemaObject> seenObjects = [];

        string action = mode == MigrationRunMode.Apply ? "Applying" : "Recording";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        foreach (MigrationDirectory m in newMigrations)
        {
            logger.LogInformation("{Action} migration {Number}/{Total}: {Directory}", action, migrationSequence + 1, newMigrations.Count, m.Name);

            MigrationDirectoryHashResult directoryHashResult = await hasher.HashDirectoryAsync(m, ct);

            MigrationDirectoryEntity dEntity = new()
            {
                MigrationRunId = run.MigrationRunId,
                Name = m.Name,
                Source = m.SourceType,
                SourceName = m.SourceName,
                Hash = directoryHashResult.DirectoryHash,
                Sequence = ++migrationSequence,
                RunDuration = 0,
            };

            long dId = await directoryRepo.InsertAsync(dEntity, transaction, ct);
            dEntity.MigrationDirectoryId = dId;

            int directoryRunDuration = 0;
            int fileSequence = 0;

            foreach (MigrationFileHashResult fileResult in directoryHashResult.Files.Values)
            {
                logger.LogDebug("{Action}: {File}", action, fileResult.Name);

                int fileDuration = 0;

                if (mode == MigrationRunMode.Apply)
                {
                    Stopwatch fSw = Stopwatch.StartNew();

                    CommandDefinition command = new(fileResult.NormalisedContent, transaction: transaction, commandTimeout: dbOptions.CommandTimeoutSeconds, cancellationToken: ct);

                    await scope.Connection.ExecuteAsync(command);

                    fSw.Stop();

                    fileDuration = (int)fSw.ElapsedMilliseconds;
                }

                directoryRunDuration += fileDuration;

                SchemaObject? target = ScriptDomUtils.GetSchemaObject(fileResult.NormalisedContent);

                if (target is null)
                    logger.LogDebug("Could not determine a target object for {File}", fileResult.Name);
                else if (seenObjects.Add(target))
                    objectsChanged.Add(target);

                MigrationFileEntity fEntity = new()
                {
                    MigrationDirectoryId = dId,
                    Name = fileResult.Name,
                    Hash = fileResult.Hash,
                    Sequence = ++fileSequence,
                    RunDuration = fileDuration,
                };

                long fId = await fileRepo.InsertAsync(fEntity, transaction, ct);

                fEntity.MigrationFileId = fId;
            }

            dEntity.RunDuration = directoryRunDuration;
            await directoryRepo.UpdateAsync(dEntity, transaction, ct);

            runDuration += directoryRunDuration;

            logger.LogDebug("Journalled migration (Directory: {DirectoryId}) - {Directory} with {Count} file(s)", dEntity.MigrationDirectoryId, m.Name, directoryHashResult.Files.Count);
        }

        return new(migrationSequence, runDuration)
        {
            TablesChanged = objectsChanged,
        };
    }
}
