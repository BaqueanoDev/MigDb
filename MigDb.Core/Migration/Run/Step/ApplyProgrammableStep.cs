using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Schema.Programmable;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Migration.Run.Step;

public sealed class ApplyProgrammableStep(
    IReadOnlyList<SchemaProgrammableFile> programmables,
    MigrationRunMode mode,
    ILogger<ApplyProgrammableStep> logger,
    SchemaProgrammableRunner programmableRunner,
    MigrationProgrammableRepository migrationProgrammableRepo) : IMigrationStep
{
    public async Task<MigrationStepResult> ExecuteAsync(MigrationRunEntity run, IDbTransaction transaction, CancellationToken ct)
    {
        SchemaProgrammableRunMode programmableMode = mode == MigrationRunMode.Apply
            ? SchemaProgrammableRunMode.Deploy
            : SchemaProgrammableRunMode.JournalOnly;

        SchemaProgrammableRunResult runResult = await programmableRunner.RunAsync(programmables, programmableMode, transaction, ct);

        if (runResult.DeployedCount == 0)
            return MigrationStepResult.Empty with { ProgrammablesExcluded = runResult.Excluded };

        Dictionary<string, SchemaProgrammableFile> fileLookup = [];

        foreach (SchemaProgrammableFile p in programmables)
            fileLookup.TryAdd($"{p.SourceType}.{p.FullName}", p);

        List<SchemaProgrammableFile> programmablesChanged = new(runResult.Deploys.Count);

        foreach (SchemaProgrammableDeploy d in runResult.Deploys)
        {
            if (fileLookup.TryGetValue($"{d.Programmable.Source}.{d.Programmable.FullName}", out SchemaProgrammableFile? file))
                programmablesChanged.Add(file);
            else
                logger.LogDebug("Could not match deployed programmable {Object} back to a source file", d.Programmable.FullName);

            d.Journal.MigrationRunId = run.MigrationRunId;

            logger.LogDebug("Applying: {File}", d.Programmable.FullName);

            await migrationProgrammableRepo.InsertAsync(d.Journal, transaction, ct);
        }

        return new(runResult.DeployedCount, runResult.RunDuration)
        {
            ProgrammablesChanged = programmablesChanged,
            ProgrammablesExcluded = runResult.Excluded,
        };
    }
}
