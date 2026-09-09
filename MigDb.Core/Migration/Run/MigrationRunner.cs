using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration.Run.Step;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace MigDb.Core.Migration.Run;

public sealed class MigrationRunner(ILogger<MigrationRunner> logger,
      SQLConnectionFactory sqlFactory,
      SchemaRepository schemaRepo,
      MigrationRunRepository runRepo,
      MigrationStepFactory stepFactory
      )
{
    public const string RecordOnlyRunMessage = "Recorded only - no migration files or programmables were executed";

    public Task<MigrationRunnerResult> RunMigrationAsync(MigrationDirectory migration, bool dryRun = false, CancellationToken ct = default)
    {
        return RunInScopeAsync(
            [stepFactory.MigrationStep([migration])],
            [stepFactory.MigrationValidationStep([migration])],
            dryRun, string.Empty, ct);
    }

    public Task<MigrationRunnerResult> RunMigrationsAsync(IReadOnlyList<MigrationDirectory> migrations, bool dryRun = false, CancellationToken ct = default)
    {
        return RunInScopeAsync(
            [stepFactory.MigrationStep(migrations)],
            [stepFactory.MigrationValidationStep(migrations)],
            dryRun, string.Empty, ct);
    }

    public Task<MigrationRunnerResult> RunProgrammableAsync(SchemaProgrammableFile programmable, bool dryRun = false, CancellationToken ct = default)
    {
        return RunInScopeAsync(
            [stepFactory.ProgrammableStep([programmable])],
            [stepFactory.ProgrammableValidationStep([programmable])],
            dryRun, string.Empty, ct);
    }

    public Task<MigrationRunnerResult> RunProgrammablesAsync(IReadOnlyList<SchemaProgrammableFile> programmables, bool dryRun = false, CancellationToken ct = default)
    {
        return RunInScopeAsync(
            [stepFactory.ProgrammableStep(programmables)],
            [stepFactory.ProgrammableValidationStep(programmables)],
            dryRun, string.Empty, ct);
    }

    public Task<MigrationRunnerResult> RunMigrationAsync(IReadOnlyList<MigrationDirectory> migrations, IReadOnlyList<SchemaProgrammableFile> programmables, bool dryRun = false, MigrationRunMode mode = MigrationRunMode.Apply, CancellationToken ct = default)
    {
        string runMessage = mode == MigrationRunMode.RecordOnly ? RecordOnlyRunMessage : string.Empty;

        return RunInScopeAsync(
            [stepFactory.MigrationStep(migrations, mode), stepFactory.ProgrammableStep(programmables, mode)],
            [stepFactory.MigrationValidationStep(migrations), stepFactory.ProgrammableValidationStep(programmables)],
            dryRun, runMessage, ct);
    }

    public async Task<MigrationRunnerResult> RunInScopeAsync(IReadOnlyList<IMigrationStep> migrationSteps, IReadOnlyList<IMigrationValidationStep> validationSteps, bool dryRun = false, string runMessage = "", CancellationToken ct = default)
    {
        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(null, ct);
        await using DbTransaction transaction = await scope.Connection.BeginTransactionAsync(ct);

        if (!await schemaRepo.MigrationSchemaInitialisedAsync(transaction, ct))
        {
            logger.LogError("Migration schema/tables are missing in the target database");
            throw new MigrationSchemaNotInitialisedException();
        }

        DateTime time = DateTime.UtcNow;
        string user = $"{Environment.UserName}@{Environment.MachineName}";

        MigrationRunEntity rEntity = new()
        {
            StartTime = time,
            CompleteTime = time,
            AppliedBy = user,
            RunResult = MigrationRunResult.None,
        };

        IDbTransaction? journal = dryRun ? transaction : null;

        List<MigrationValidationResult> failures = [];

        foreach (IMigrationValidationStep step in validationSteps)
            failures.AddRange(await step.ExecuteAsync(transaction, ct));

        if (failures.Count != 0)
        {
            IEnumerable<string> described = failures.Select(x => x.Describe());

            rEntity.RunResult = MigrationRunResult.ValidationFailed;
            rEntity.RunResultMessage = $"One or more validation errors occurred: {string.Join("; ", described)}";

            await runRepo.InsertAsync(rEntity, journal, ct);
            await transaction.RollbackAsync(ct);

            return new(MigrationRunResult.ValidationFailed, 0) { Validations = failures };
        }

        int databaseLockResult = await schemaRepo.AcquireMigrationLockAsync(transaction, ct);

        if (databaseLockResult < 0)
        {
            logger.LogError("Could not acquire migration lock (result {Result}) - another migration may be in progress", databaseLockResult);
            throw new MigrationAppLockException(databaseLockResult);
        }

        long rId = await runRepo.InsertAsync(rEntity, journal, ct);

        MigrationRunnerResult result;

        try
        {
            result = await ExecuteAsync(rEntity, migrationSteps, transaction, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} failed - rolling back", rId);

            await transaction.RollbackAsync(CancellationToken.None);
            await CompleteRunAsync(rEntity, MigrationRunResult.Failed, ex.Message, 0, dryRun);

            throw;
        }

        if (dryRun)
        {
            logger.LogInformation("Dry run - rolling back run {RunId}", rId);
            await transaction.RollbackAsync(ct);
        }
        else
        {
            logger.LogInformation("Committing run {RunId}", rId);
            await transaction.CommitAsync(ct);
        }

        await CompleteRunAsync(rEntity, result.Result, runMessage, result.Duration, dryRun);

        return result;
    }

    private async Task<MigrationRunnerResult> ExecuteAsync(MigrationRunEntity run, IReadOnlyList<IMigrationStep> migrationSteps, IDbTransaction transaction, CancellationToken ct)
    {
        int duration = 0;
        int changes = 0;

        List<SchemaObject> tablesChanged = [];
        List<SchemaProgrammableFile> programmablesChanged = [];
        List<SchemaProgrammableFile> programmablesExcluded = [];

        foreach (IMigrationStep step in migrationSteps)
        {
            MigrationStepResult r = await step.ExecuteAsync(run, transaction, ct);

            duration += r.Duration;
            changes += r.Changes;

            tablesChanged.AddRange(r.TablesChanged);
            programmablesChanged.AddRange(r.ProgrammablesChanged);
            programmablesExcluded.AddRange(r.ProgrammablesExcluded);
        }

        return new(MigrationRunResult.Success, duration)
        {
            Changes = changes,
            TablesChanged = tablesChanged,
            ProgrammablesChanged = programmablesChanged,
            ProgrammablesExcluded = programmablesExcluded,
        };
    }

    private async Task CompleteRunAsync(MigrationRunEntity run, MigrationRunResult result, string message, int duration, bool dryRun)
    {
        run.CompleteTime = DateTime.UtcNow;
        run.RunDuration = duration;
        run.RunResult = result;
        run.RunResultMessage = message;

        if (dryRun)
            return;

        await runRepo.UpdateAsync(run, null, CancellationToken.None);
    }
}
