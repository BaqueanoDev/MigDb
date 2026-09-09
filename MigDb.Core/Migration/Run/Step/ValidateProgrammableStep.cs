using MigDb.Core.Migration.Validation;
using MigDb.Core.Schema.Programmable;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Migration.Run.Step;

public sealed class ValidateProgrammableStep(IReadOnlyList<SchemaProgrammableFile> programmables, ILogger<ValidateProgrammableStep> logger, MigrationValidator validator) : IMigrationValidationStep
{
    public Task<IReadOnlyList<MigrationValidationResult>> ExecuteAsync(IDbTransaction transaction, CancellationToken ct)
    {
        List<MigrationValidationResult> failures = [];

        // every programmable is checked, stopping at the first bad one would hide the rest
        foreach (SchemaProgrammableFile p in programmables)
        {
            ct.ThrowIfCancellationRequested();

            // ValidateScript only sees the sql, the file it came from is attributed here
            MigrationValidationResult validation = validator.ValidateProgrammableScript(p.Script) with { Subject = p.FullName };

            if (validation.IsValid)
                continue;

            logger.LogError("Validation failed for {File}: {Errors}", p.FullName, validation.Errors);
            failures.Add(validation);
        }

        return Task.FromResult<IReadOnlyList<MigrationValidationResult>>(failures);
    }
}
