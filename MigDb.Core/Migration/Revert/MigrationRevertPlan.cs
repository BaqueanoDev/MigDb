using MigDb.Core.Entities;

namespace MigDb.Core.Migration.Revert;

public sealed record MigrationRevertPlan(
    IReadOnlyList<MigrationDirectoryEntity> Directories,
    IReadOnlyList<MigrationFileEntity> Files,
    IReadOnlyList<MigrationRunEntity> Runs,
    IReadOnlyList<MigrationProgrammableEntity> ProgrammableEntries,
    IReadOnlyList<ProgrammableRewind> Programmables)
{
    public static MigrationRevertPlan Empty { get; } = new([], [], [], [], []);
}
