using MigDb.Core.Entities;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;

namespace MigDb.Core.Migration.Run;

public sealed record MigrationRunnerResult(MigrationRunResult Result, int Duration)
{
    public int Changes { get; init; }

    public IReadOnlyList<SchemaObject> TablesChanged { get; init; } = [];
    public IReadOnlyList<SchemaProgrammableFile> ProgrammablesChanged { get; init; } = [];
    public IReadOnlyList<SchemaProgrammableFile> ProgrammablesExcluded { get; init; } = [];
    public IReadOnlyList<MigrationValidationResult> Validations { get; init; } = [];
}
