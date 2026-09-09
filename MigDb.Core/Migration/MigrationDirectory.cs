using MigDb.Core.Entities;
using MigDb.Core.Migration.Manifest;

namespace MigDb.Core.Migration;

/// <summary>
/// Represents a migration directory NOT Entity
/// </summary>
/// <param name="Info">Migration directory info</param>
/// <param name="Source">Directory source</param>
public record MigrationDirectory(DirectoryInfo Info, MigrationSource Source)
{
    public MigrationDirectory(string path, MigrationSource source)
        : this(new DirectoryInfo(path), source) { }

    public string Name => Info.Name;
    public string FullName => Info.FullName;

    /// <summary>
    /// Flattened source for persistence, the entity/journal columns
    /// </summary>
    public MigrationSourceType SourceType => MigrationSource.ToMigrationSourceType(Source);

    /// <summary>
    /// Flattened source parameter for persistence, e.g. project name. Null for Common/External
    /// </summary>
    public string? SourceName => MigrationSource.ToSourceName(Source);

    public FileInfo ManifestInfo => new(Path.Join(Info.FullName, MigrationManifest.FileName));

    public IReadOnlyList<FileInfo> GetDataFiles()
    {
        List<FileInfo> files = [.. Info.GetFiles()
                        .Where(f => !f.Name.Equals(MigrationManifest.FileName, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f.Name, StringComparer.Ordinal)];

        return files;
    }
}
