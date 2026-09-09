namespace MigDb.Core.Schema.Programmable;

public sealed record SchemaProgrammableComparisonResult(
    IReadOnlyList<SchemaProgrammableFile> Added,
    IReadOnlyList<SchemaProgrammableFile> Changed,
    IReadOnlyList<SchemaProgrammableFile> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Changed.Count == 0 && Removed.Count == 0;
}
