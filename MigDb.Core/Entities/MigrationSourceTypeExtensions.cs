using MigDb.Core.Migration;

namespace MigDb.Core.Entities;

public static class MigrationSourceTypeExtensions
{
    /// <summary>
    /// Rebuild the source union from its persisted/bound enum form
    /// </summary>
    /// <param name="value">Source type</param>
    /// <param name="project">Project name, required when <paramref name="value"/> is Project</param>
    /// <returns>The matching migration source</returns>
    /// <exception cref="ArgumentException">Thrown when Project is requested without a name</exception>
    public static MigrationSource ToMigrationSource(this MigrationSourceType value, string? project = null) => value switch
    {
        MigrationSourceType.Common => new MigrationSource.Common(),
        MigrationSourceType.External => new MigrationSource.External(),
        MigrationSourceType.Project when string.IsNullOrWhiteSpace(project)
            => throw new ArgumentException("Project name is required when source is Project", nameof(project)),
        MigrationSourceType.Project => new MigrationSource.Project(project),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
