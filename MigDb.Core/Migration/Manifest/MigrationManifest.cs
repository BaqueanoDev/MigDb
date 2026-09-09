using System.Text.Json.Serialization;

namespace MigDb.Core.Migration.Manifest;

/// <summary>
/// Represents a serializable migration object that gets
/// written file when preparing
/// 
/// </summary>
/// <param name="Files">Files contained by manifest</param>
/// <param name="Hash">Migration hash</param>
public sealed record MigrationManifest(IReadOnlyList<MigrationManifestFile> Files, string Hash)
{
    public const string FileName = "manifest.json";

    [JsonPropertyOrder(-1)]
    public int Version { get; init; } = 1;
}
