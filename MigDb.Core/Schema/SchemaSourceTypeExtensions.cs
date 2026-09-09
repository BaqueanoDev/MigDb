namespace MigDb.Core.Schema;

public static class SchemaSourceTypeExtensions
{
    /// <summary>
    /// Rebuild the source union from its persisted/bound enum form
    /// </summary>
    /// <param name="value">Source type</param>
    /// <param name="project">Project name, required when <paramref name="value"/> is Project</param>
    /// <returns>The matching schema source</returns>
    /// <exception cref="ArgumentException">Thrown when Project is requested without a name</exception>
    public static SchemaSource ToSchemaSource(this SchemaSourceType value, string? project = null) => value switch
    {
        SchemaSourceType.Common => new SchemaSource.Common(),
        SchemaSourceType.Project when string.IsNullOrWhiteSpace(project)
            => throw new ArgumentException("Project name is required when source is Project", nameof(project)),
        SchemaSourceType.Project => new SchemaSource.Project(project),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
