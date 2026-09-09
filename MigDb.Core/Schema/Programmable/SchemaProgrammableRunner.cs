using Dapper;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Options;
using MigDb.Core.Utils;
using MigDb.Core.Utils.ScriptDom;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;

namespace MigDb.Core.Schema.Programmable;

public sealed class SchemaProgrammableRunner(ILogger<SchemaProgrammableRunner> logger,
    SchemaProgrammableScanner loader,
    SchemaProgrammableRepository schemaProgrammableRepo,
    SchemaRepository schemaRepo,
    SQLConnectionFactory sqlFactory,
    DatabaseOptions dbOptions)
{
    public async Task<SchemaProgrammableRunResult> RunAsync(IReadOnlyList<SchemaProgrammableFile> files, SchemaProgrammableRunMode mode, IDbTransaction transaction, CancellationToken ct = default)
    {
        IReadOnlyList<SchemaProgrammableEntity> deployedList = await schemaProgrammableRepo.GetAllAsync(transaction, ct);
        Dictionary<string, SchemaProgrammableEntity> deployedLookup = deployedList.ToDictionary(x => $"{x.Source}.{x.FullName}");

        IReadOnlyList<SchemaProgrammableBinding> unEditable = await schemaRepo.GetUnEditableProgrammablesAsync(transaction, ct);

        Dictionary<string, SchemaProgrammableExclusion> unalterable = new(StringComparer.OrdinalIgnoreCase);

        foreach (SchemaProgrammableBinding u in unEditable)
        {
            if (u.Reason == SchemaProgrammableExclusion.IndexedView || !unalterable.ContainsKey(u.FullName))
                unalterable[u.FullName] = u.Reason;
        }

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        int created = 0;
        int updated = 0;
        int skipped = 0;
        int runDuration = 0;
        List<SchemaProgrammableDeploy> deploys = [];
        List<SchemaProgrammableFile> excluded = [];

        // need a way of detecting deployments vs journalling only
        string action = mode == SchemaProgrammableRunMode.Deploy ? "Deployed" : "Journalled";

        logger.LogDebug("Found {Objects} files, running in {Mode} mode", files.Count, mode);

        foreach (SchemaProgrammableFile f in files)
        {
            SchemaProgrammableEntity? deployed = deployedLookup.GetValueOrDefault($"{f.SourceType}.{f.FullName}");

            SchemaProgrammableExclusion exclusion = f.Exclusion;

            if (exclusion == SchemaProgrammableExclusion.None)
                exclusion = unalterable.GetValueOrDefault(f.FullName);

            if (exclusion != SchemaProgrammableExclusion.None)
            {
                logger.LogWarning("Programmable {Object} is {Exclusion} and cannot be redeployed with CREATE OR ALTER - change it through a migration", f.FullName, exclusion);

                if (deployed is not null && !deployed.Hash.SequenceEqual(f.Hash))
                    logger.LogWarning("Journal still holds an older hash for {Object} from before it was excluded - that row is now stale", f.FullName);

                excluded.Add(f with { Exclusion = exclusion });
                continue;
            }

            if (deployed is not null && deployed.Hash.SequenceEqual(f.Hash))
            {
                logger.LogDebug("Programmable {Object} unchanged, skipping...", f.FullName);
                skipped++;
                continue;
            }

            if (mode == SchemaProgrammableRunMode.Deploy)
                logger.LogInformation("Applying programmable: {Object} from ({File})", f.FullName, f.FileInfo.Name);

            int runTime = mode switch
            {
                SchemaProgrammableRunMode.Deploy => await ExecuteAsync(scope, ScriptDomUtils.ToCreateOrAlter(f.Script), transaction, ct),
                SchemaProgrammableRunMode.JournalOnly => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown programmable run mode"),
            };

            runDuration += runTime;

            SchemaProgrammableEntity p;
            byte[]? hashBefore;

            // persist missing programmables
            if (deployed is null)
            {
                SchemaProgrammableEntity e = new()
                {
                    Schema = f.Schema,
                    ObjectName = f.ObjectName,
                    Type = f.Type,
                    Source = f.SourceType,
                    SourceName = f.SourceName,
                    Hash = f.Hash
                };

                await schemaProgrammableRepo.InsertAsync(e, transaction, ct);

                p = e;
                hashBefore = null;
                created++;

                logger.LogDebug("{Action} new programmable {Object}", action, f.FullName);
            }
            else
            {
                // update hash in case its changed
                // TODO: maybe type
                deployed.Hash = f.Hash;
                deployed.SourceName = f.SourceName;

                // the update hands back what it overwrote, so the journal cannot pick up the new hash
                hashBefore = await schemaProgrammableRepo.UpdateAsync(deployed, transaction, ct);

                p = deployed;
                updated++;

                logger.LogDebug("{Action} changed programmable {Object}", action, f.FullName);
            }

            MigrationProgrammableEntity j = new()
            {
                SchemaProgrammableId = p.SchemaProgrammableId,
                HashBefore = hashBefore,
                HashAfter = f.Hash,
                RunDuration = runTime,
            };

            deploys.Add(new SchemaProgrammableDeploy(p, j));
        }

        SchemaProgrammableRunResult result = new(deploys, created, updated, skipped, runDuration)
        {
            Excluded = excluded,
        };

        return result;
    }

    public async Task<SchemaProgrammableRunResult> RunBySourceAsync(SchemaSource source, SchemaProgrammableRunMode mode, IDbTransaction transaction, CancellationToken ct = default)
    {
        logger.LogDebug("Attempting to run programmables from {Source}", source);

        IReadOnlyList<SchemaProgrammableFile> pFiles = await loader.LoadBySourceAsync(source, ct);

        SchemaProgrammableRunResult result = await RunAsync(pFiles, mode, transaction, ct);

        return result;
    }

    private async Task<int> ExecuteAsync(SQLConnectionLease scope, string script, IDbTransaction transaction, CancellationToken ct)
    {
        IReadOnlyList<string> batches = DacFxUtils.SplitScriptByBatch(script);

        Stopwatch sw = Stopwatch.StartNew();

        foreach (string batch in batches)
        {
            CommandDefinition command = new(batch, transaction: transaction, commandTimeout: dbOptions.CommandTimeoutSeconds, cancellationToken: ct);

            await scope.Connection.ExecuteAsync(command);
        }

        sw.Stop();

        return (int)sw.ElapsedMilliseconds;
    }
}
