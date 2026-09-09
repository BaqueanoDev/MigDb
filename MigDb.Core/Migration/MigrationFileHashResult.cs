namespace MigDb.Core.Migration;

public sealed record MigrationFileHashResult(string Name, string NormalisedContent, byte[] Hash);
