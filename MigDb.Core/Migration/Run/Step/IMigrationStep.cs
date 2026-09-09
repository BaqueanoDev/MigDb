using MigDb.Core.Entities;
using System.Data;

namespace MigDb.Core.Migration.Run.Step;

public interface IMigrationStep
{
    Task<MigrationStepResult> ExecuteAsync(MigrationRunEntity run, IDbTransaction transaction, CancellationToken ct);
}
