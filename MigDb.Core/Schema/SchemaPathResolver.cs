using MigDb.Core.Migration;
using MigDb.Core.Options;
using Microsoft.Extensions.Logging;

namespace MigDb.Core.Schema;

public sealed class SchemaPathResolver(ILogger<SchemaPathResolver> logger, MigrationOptions options)
{
    public const string CommonSchemaDirectoryName = "Common";
    public const string ProjectsSchemaDirectoryName = "Projects";
    public const string SchemaDirectoryName = "Schema";

    public string SchemaRootPath => Path.Join(options.SchemaProjectPath, SchemaDirectoryName);
    public string CommonSchemaPath => Path.Join(SchemaRootPath, CommonSchemaDirectoryName);
    public string ProjectsSchemaPath => Path.Join(SchemaRootPath, ProjectsSchemaDirectoryName);

    public string GetProjectPath(string project)
    {
        logger.LogTrace("Looking for project schema path: {Project}", project);

        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        return Path.Join(ProjectsSchemaPath, project);
    }

    public string ResolveSchemaSourceRoot(SchemaSource source)
    {
        logger.LogTrace("Attempting to resolve root for source: {source}", SchemaSource.ToSchemaSourceType(source));

        return source switch
        {
            SchemaSource.Common => CommonSchemaPath,
            SchemaSource.Project x => GetProjectPath(x.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
    }

    public string GetSchemaPath(SchemaSource source, string Schema)
    {
        logger.LogTrace("Attempting to resolve Schema: {Schema} from source {source}", Schema, SchemaSource.ToSchemaSourceType(source));

        ArgumentException.ThrowIfNullOrWhiteSpace(Schema);

        string root = ResolveSchemaSourceRoot(source);
        string path = Path.Join(root, Schema);

        return path;
    }

    public FileInfo? FindSchemaProject(string root)
    {
        logger.LogTrace("Attempting to resolve project file in {Root}", root);

        return new DirectoryInfo(root).EnumerateFiles("*.sqlproj").FirstOrDefault();
    }
}
