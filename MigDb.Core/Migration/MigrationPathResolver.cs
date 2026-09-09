using MigDb.Core.Options;
using Microsoft.Extensions.Logging;

namespace MigDb.Core.Migration;

/// <summary>
/// Used to resolve paths for migration directories
/// </summary>
/// <param name="logger">Logger to use</param>
/// <param name="options">Migration options to use</param>
public sealed class MigrationPathResolver(ILogger<MigrationPathResolver> logger, MigrationOptions options)
{
    public const string CommonScriptDirectoryName = "Common";
    public const string ProjectsDirectoryName = "Projects";
    public const string MigrationDirectoryName = "Migrations";

    /// <summary>
    /// Absolute path to directory holding Common migrations
    /// </summary>
    public string CommonRootPath => Path.Join(options.SchemaProjectPath, MigrationDirectoryName, CommonScriptDirectoryName);

    /// <summary>
    /// Absolute path to directory holding all project migrations
    /// </summary>
    public string ProjectsRootPath => Path.Join(options.SchemaProjectPath, MigrationDirectoryName, ProjectsDirectoryName);

    /// <summary>
    /// Resolve the migration root path for a single project
    /// </summary>
    /// <param name="project">Project name</param>
    /// <returns>Absolute path to the project's migration root</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="project"/> is null or empty</exception>
    public string GetProjectPath(string project)
    {
        logger.LogTrace("Looking for project path: {Project}", project);

        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        return Path.Join(ProjectsRootPath, project);
    }

    /// <summary>
    /// Resolve the root path for a migration source (Common or a specific project)
    /// </summary>
    /// <param name="source">Migration source to resolve</param>
    /// <returns>Absolute path to the source's migration root</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="source"/> is an unrecognised source type</exception>
    public string ResolveMigrationSourceRoot(MigrationSource source)
    {
        logger.LogTrace("Attempting to resolve root for source: {source}", MigrationSource.ToMigrationSourceType(source));

        return source switch
        {
            MigrationSource.Common => CommonRootPath,
            MigrationSource.Project x => GetProjectPath(x.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
    }

    /// <summary>
    /// Build the absolute path to a named migration within a source
    /// </summary>
    /// <param name="source">Migration source the migration belongs to</param>
    /// <param name="migration">Migration directory name</param>
    /// <returns>Absolute path to the migration directory (not guaranteed to exist)</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="migration"/> is null or empty</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="source"/> is an unrecognised source type</exception>
    public string GetMigrationPath(MigrationSource source, string migration)
    {
        logger.LogTrace("Attempting to resolve migration: {migration} from source {source}", migration, MigrationSource.ToMigrationSourceType(source));

        ArgumentException.ThrowIfNullOrWhiteSpace(migration);

        string root = ResolveMigrationSourceRoot(source);
        string path = Path.Join(root, migration);

        return path;
    }
}
