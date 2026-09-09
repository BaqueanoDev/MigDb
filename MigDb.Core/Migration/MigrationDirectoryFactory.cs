using Microsoft.Extensions.Logging;

namespace MigDb.Core.Migration;

/// <summary>
/// Handles MigrationDirectory objects
/// 
/// Originally was part of path resolver
/// </summary>
/// <param name="logger">Logger to use</param>
/// <param name="pathResolver">Path resolver service</param>
public sealed partial class MigrationDirectoryFactory(ILogger<MigrationDirectoryFactory> logger, MigrationPathResolver pathResolver)
{
    public const string TimestampFormat = "yyyyMMdd_HHmmss";

    /// <summary>
    /// Resolve a migration from source
    /// 
    /// Intentionally internal, use MigrationSelector
    /// </summary>
    /// <param name="source">Source migration belongs to</param>
    /// <param name="name">Migration directory name</param>
    /// <returns>The migration directory for the resolved path</returns>
    internal MigrationDirectory Get(MigrationSource source, string name)
    {
        string path = pathResolver.GetMigrationPath(source, name);

        logger.LogDebug("Resolving migration directory {Name} for {Source}", name, MigrationSource.ToMigrationSourceType(source));

        return new MigrationDirectory(path, source);
    }

    public MigrationDirectory GetCommon(string name)
    {
        return Get(new MigrationSource.Common(), name);
    }

    public MigrationDirectory GetProject(string projectName, string name)
    {
        return Get(new MigrationSource.Project(projectName), name);
    }

    /// <summary>
    /// Get all migration directories for a source, ordered by name
    /// </summary>
    /// <param name="source">Migration source to enumerate</param>
    /// <returns>Orderered migration directories found under the source root</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="source"/> is an unrecognised source type</exception>
    public List<MigrationDirectory> GetMigrationDirectories(MigrationSource source)
    {
        string root = pathResolver.ResolveMigrationSourceRoot(source);

        logger.LogDebug("Looking for migration directories in {Directory}", root);

        DirectoryInfo mDirectory = new(root);

        if (!mDirectory.Exists)
        {
            logger.LogWarning("Migration directory missing: {Directory}", root);
            return [];
        }

        List<MigrationDirectory> result = mDirectory.GetDirectories()
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => new MigrationDirectory(x, source))
            .ToList();

        foreach (MigrationDirectory l in result)
            logger.LogDebug("Found {Directory}", l.Name);

        return result;
    }

    /// <summary>
    /// Create a new timestamped migration directory under a source
    /// </summary>
    /// <param name="source">Migration source to create the directory under</param>
    /// <param name="name">Migration name (must not contain a path separator)</param>
    /// <returns>The created MigrationDirectory</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null, empty, or contains a path separator</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="source"/> is an unrecognised source type</exception>
    /// <exception cref="InvalidOperationException">Thrown when the target directory already exists</exception>
    public MigrationDirectory CreateMigrationDirectory(MigrationSource source, string? name)
    {
        if (name is not null)
        {
            bool isPath = name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar);

            if (isPath)
                throw new ArgumentException("Detected path in name");
        }

        logger.LogDebug("Creating migration directory {Name} for {Source}", name, source);

        string root = pathResolver.ResolveMigrationSourceRoot(source);
        DirectoryInfo info = CreateMigrationDirectoryAt(root, name);

        return new MigrationDirectory(info, source);
    }

    /// <summary>
    /// Create a timestamped migration directory at an explicit path
    /// </summary>
    /// <param name="path">Parent path to create the directory under</param>
    /// <param name="name">Optional migration name appended after the timestamp</param>
    /// <returns>DirectoryInfo for the created directory</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty</exception>
    /// <exception cref="InvalidOperationException">Thrown when the target directory already exists</exception>
    public static DirectoryInfo CreateMigrationDirectoryAt(string path, string? name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string directoryName = $"{DateTime.UtcNow.ToString(TimestampFormat)}";

        if (!string.IsNullOrWhiteSpace(name))
            directoryName += $"_{name}";

        string newPath = Path.Join(path, directoryName);

        if (Directory.Exists(newPath))
            throw new InvalidOperationException("Directory already exists");

        DirectoryInfo info = Directory.CreateDirectory(newPath);

        return info;
    }
}
