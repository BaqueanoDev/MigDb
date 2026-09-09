
namespace MigDb.Core.Migration;

public sealed record MigrationDirectoryHashResult(byte[] DirectoryHash, IReadOnlyDictionary<string, MigrationFileHashResult> Files);
