using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;

namespace MigDb.Core.Schema.Programmable;

/// <summary>
/// Loads schema programmable entities with full navigation.
///
/// Kept separate from <c>MigrationLoader</c> because a schema programmable is a
/// schema-domain object, not a migration - a run's deploys hang off the run, but
/// an object's deploy history is rooted at the object itself.
/// </summary>
/// <param name="logger">Logger to use</param>
/// <param name="schemaProgrammableRepo">Schema programmable (current-state) repo</param>
/// <param name="programmableRepo">Migration programmable (per-run deploy) repo</param>
/// <param name="runRepo">Migration run repo</param>
public sealed class SchemaProgrammableLoader(ILogger<SchemaProgrammableLoader> logger, SchemaProgrammableRepository schemaProgrammableRepo, MigrationProgrammableRepository programmableRepo, MigrationRunRepository runRepo)
{
    /// <summary>
    /// Used to get a fully populated SchemaProgrammableEntity
    ///
    /// Fans out to its deploy history (<see cref="SchemaProgrammableEntity.Migrations"/>),
    /// each row back-referenced to this object and its parent run
    /// </summary>
    /// <param name="schemaProgrammableId">Id of schema programmable to fetch</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>If found a traversable SchemaProgrammableEntity else null</returns>
    public async Task<SchemaProgrammableEntity?> GetSchemaProgrammableTreeAsync(long schemaProgrammableId, CancellationToken ct = default)
    {
        logger.LogDebug("Getting schema programmable tree: {SchemaProgrammable}", schemaProgrammableId);

        SchemaProgrammableEntity? schemaProgrammable = await schemaProgrammableRepo.GetByIdAsync(schemaProgrammableId, ct: ct);

        if (schemaProgrammable is null)
        {
            logger.LogDebug("Schema programmable not found");
            return null;
        }

        logger.LogDebug("Getting deploy history for schema programmable: {SchemaProgrammable}", schemaProgrammable.FullName);

        IReadOnlyList<MigrationProgrammableEntity> programmableList = await programmableRepo.GetBySchemaProgrammableIdAsync(schemaProgrammableId, ct: ct);

        // batch load the run each deploy row belongs to
        List<long> runIds = programmableList
            .Select(p => p.MigrationRunId)
            .Distinct()
            .ToList();

        IReadOnlyList<MigrationRunEntity> runs = await runRepo.GetByIdListAsync(runIds, ct: ct);
        Dictionary<long, MigrationRunEntity> runsById = runs.ToDictionary(r => r.MigrationRunId);

        foreach (MigrationProgrammableEntity programmable in programmableList)
        {
            programmable.SchemaProgrammable = schemaProgrammable;

            if (runsById.TryGetValue(programmable.MigrationRunId, out MigrationRunEntity? run))
                programmable.MigrationRun = run;
        }

        schemaProgrammable.Migrations = [.. programmableList];

        logger.LogDebug("Found: {Programmables} deploy(s) for schema programmable {SchemaProgrammable}", schemaProgrammable.Migrations.Count, schemaProgrammable.FullName);

        return schemaProgrammable;
    }
}
