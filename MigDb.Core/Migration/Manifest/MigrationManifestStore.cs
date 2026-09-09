using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MigDb.Core.Migration.Manifest;

public sealed class MigrationManifestStore(MigrationHasher hasher, ILogger<MigrationManifestStore> logger)
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Build a manifest capturing the directory's data file names and hash
    /// </summary>
    /// <param name="directory">The migration directory to capture</param>
    /// <returns>The created manifest</returns>
    public async Task<MigrationManifest> CreateManifestAsync(MigrationDirectory directory, CancellationToken ct = default)
    {
        logger.LogDebug("Creating manifest for: {Directory}", directory.Name);

        MigrationDirectoryHashResult hashResult = await hasher.HashDirectoryAsync(directory, ct);

        List<MigrationManifestFile> manifestFiles = [.. hashResult.Files.Values.Select(x => new MigrationManifestFile(x.Name, Convert.ToHexString(x.Hash)))];

        return new MigrationManifest(manifestFiles, Convert.ToHexString(hashResult.DirectoryHash));
    }

    /// <summary>
    /// Serialise a manifest and write to file migration directory
    /// </summary>
    /// <param name="directory">The migration directory to write into</param>
    /// <param name="manifest">The manifest to write</param>
    public void WriteManifest(MigrationDirectory directory, MigrationManifest manifest)
    {
        logger.LogDebug("Writing manifest to: {Directory}", directory.Name);

        if (directory.ManifestInfo.Exists)
            logger.LogWarning("Detected existing manifest in: {Directory} will override", directory.Name);

        string json = JsonSerializer.Serialize(manifest, _serializerOptions);

        File.WriteAllText(directory.ManifestInfo.FullName, json);
    }

    /// <summary>
    /// Load the manifest from a directory if one is present
    /// </summary>
    /// <param name="directory">The migration directory to read from</param>
    /// <returns>The manifest, or null when no manifest file exists</returns>
    /// <exception cref="InvalidOperationException">Thrown when the manifest file exists but deserialises to null</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the manifest file contains invalid JSON</exception>
    public async Task<MigrationManifest?> LoadManifestAsync(MigrationDirectory directory)
    {
        FileInfo file = directory.ManifestInfo;

        logger.LogDebug("Loading manifest from: {Directory}", directory.Name);

        if (!file.Exists)
        {
            logger.LogDebug("No manifest found in {Directory}", directory.Name);
            return null;
        }

        string json = await File.ReadAllTextAsync(file.FullName);

        MigrationManifest? manifest = JsonSerializer.Deserialize<MigrationManifest>(json, _serializerOptions);

        if (manifest is null)
            throw new InvalidOperationException($"Invalid manifest in {directory.Name}");

        return manifest;
    }
}
