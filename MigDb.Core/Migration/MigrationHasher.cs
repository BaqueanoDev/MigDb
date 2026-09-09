using MigDb.Core.Utils;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace MigDb.Core.Migration;

public sealed class MigrationHasher(ILogger<MigrationHasher> logger)
{
    /// <summary>
    /// Hash a single file over its name and normalised content: line endings collapsed
    /// to LF, trailing whitespace stripped, and a single trailing newline
    /// enforced so whitespace-only edits do not change the hash.
    /// </summary>
    /// <param name="file">The file to hash</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The file hash and normalised content</returns>
    public async Task<MigrationFileHashResult> HashFileAsync(FileInfo file, CancellationToken ct = default)
    {
        logger.LogDebug("Hashing file {File}", file.Name);

        string rawContent = await File.ReadAllTextAsync(file.FullName, ct);
        string normalised = StringUtils.Normalise(rawContent);
        byte[] hash = HashUtils.SHA256([file.Name, normalised]);

        return new MigrationFileHashResult(file.Name, normalised, hash);
    }

    /// <summary>
    /// Hash a directory over its name and each data file's name and content,
    /// giving a single hash that covers the whole migration.
    /// </summary>
    /// <param name="directory">The migration directory to hash</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The SHA-256 hash of the directory</returns>
    public async Task<MigrationDirectoryHashResult> HashDirectoryAsync(MigrationDirectory directory, CancellationToken ct = default)
    {
        logger.LogDebug("Hashing directory {Directory}", directory.Name);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        IReadOnlyList<FileInfo> files = directory.GetDataFiles();

        Dictionary<string, MigrationFileHashResult> fileResults = new(files.Count);

        byte[] dNameHash = HashUtils.SHA256($"{directory.Name}");
        hash.AppendData(dNameHash);

        foreach (FileInfo f in files)
        {
            MigrationFileHashResult fileResult = await HashFileAsync(f, ct);
            hash.AppendData(fileResult.Hash);
            fileResults.Add(f.Name, fileResult);
        }

        byte[] directoryHash = hash.GetHashAndReset();

        return new MigrationDirectoryHashResult(directoryHash, fileResults);
    }
}
