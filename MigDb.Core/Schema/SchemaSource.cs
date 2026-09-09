namespace MigDb.Core.Schema;

public abstract record SchemaSource
{
    private SchemaSource() { }

    public sealed record Common : SchemaSource;

    public sealed record Project(string Name) : SchemaSource;

    public static SchemaSourceType ToSchemaSourceType(SchemaSource source)
    {
        return source switch
        {
            Common => SchemaSourceType.Common,
            Project => SchemaSourceType.Project,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
    }

    public static string? ToSourceName(SchemaSource source)
    {
        return source switch
        {
            Project p => p.Name,
            _ => null,
        };
    }
}
