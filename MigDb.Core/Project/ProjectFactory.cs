using MigDb.Core.Migration;
using MigDb.Core.Options;
using MigDb.Core.Schema;
using Microsoft.Extensions.Logging;

namespace MigDb.Core.Project;

public sealed class ProjectFactory(ILogger<ProjectFactory> logger, MigrationOptions options)
{
    public List<ProjectDirectory> CreateProject()
    {
        if (string.IsNullOrWhiteSpace(options.SchemaProjectPath))
            throw new InvalidOperationException("Schema project path missing from config");

        logger.LogDebug("Creating project structure from config path");

        return CreateProjectAt(options.SchemaProjectPath);
    }

    public List<ProjectDirectory> CreateProjectAt(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string migrationRoot = Path.Join(path, MigrationPathResolver.MigrationDirectoryName);
        string schemaRoot = Path.Join(path, SchemaPathResolver.SchemaDirectoryName);

        string[] structure = [
            path,
            migrationRoot,
            Path.Join(migrationRoot, MigrationPathResolver.CommonScriptDirectoryName),
            Path.Join(migrationRoot, MigrationPathResolver.ProjectsDirectoryName),
            schemaRoot,
            Path.Join(schemaRoot, SchemaPathResolver.CommonSchemaDirectoryName),
            Path.Join(schemaRoot, SchemaPathResolver.ProjectsSchemaDirectoryName),
        ];

        List<ProjectDirectory> result = [.. structure.Select(CreateDirectory)];

        int failed = result.Count(x => x.Status == ProjectDirectoryStatus.Failed);

        if (failed > 0)
            logger.LogError("Project structure incomplete, {Failed} of {Total} directories failed", failed, result.Count);

        return result;
    }

    private ProjectDirectory CreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            logger.LogTrace("Directory already exists: {Directory}", path);

            return new ProjectDirectory(new DirectoryInfo(path), ProjectDirectoryStatus.Existed);
        }

        try
        {
            Directory.CreateDirectory(path);

            logger.LogDebug("Created directory: {Directory}", path);

            return new ProjectDirectory(new DirectoryInfo(path), ProjectDirectoryStatus.Created);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create directory {Directory}: {ex}", path, ex);

            return new ProjectDirectory(new DirectoryInfo(path), ProjectDirectoryStatus.Failed);
        }
    }
}
