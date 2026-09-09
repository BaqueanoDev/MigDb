using MigDb.Core.Migration.Validation;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Migration.Run.Step;

public sealed class ValidateMigrationDirectoryStep(IReadOnlyList<MigrationDirectory> migrations, ILogger<ValidateMigrationDirectoryStep> logger, MigrationValidator validator) : IMigrationValidationStep
{
    public async Task<IReadOnlyList<MigrationValidationResult>> ExecuteAsync(IDbTransaction transaction, CancellationToken ct)
    {
        List<MigrationValidationResult> failures = [];

        // every directory is checked, stopping at the first bad one would hide the rest
        foreach (MigrationDirectory m in migrations)
        {
            MigrationValidationResult validation = await validator.ValidateDirectoryAsync(m, ct);

            if (validation.IsValid)
                continue;

            logger.LogError("Validation failed for {Directory}: {Errors}", m.Name, validation.Errors);
            failures.Add(validation);
        }

        return failures;
    }
}
