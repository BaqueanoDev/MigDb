using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;

namespace MigDb.Core.Migration.Run.Step;

public sealed record MigrationStepResult(int Changes, int Duration)
{
    public IReadOnlyList<SchemaObject> TablesChanged { get; init; } = [];
    public IReadOnlyList<SchemaProgrammableFile> ProgrammablesChanged { get; init; } = [];
    public IReadOnlyList<SchemaProgrammableFile> ProgrammablesExcluded { get; init; } = [];

    public static MigrationStepResult Empty { get; } = new(0, 0);
}
