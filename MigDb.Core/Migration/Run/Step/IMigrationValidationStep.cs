using MigDb.Core.Migration.Validation;
using System.Data;

namespace MigDb.Core.Migration.Run.Step;

public interface IMigrationValidationStep
{
    /// <summary>
    /// Validates everything the step covers
    /// </summary>
    /// <returns>One result per failing subject, empty when everything is valid</returns>
    Task<IReadOnlyList<MigrationValidationResult>> ExecuteAsync(IDbTransaction transaction, CancellationToken ct);
}
