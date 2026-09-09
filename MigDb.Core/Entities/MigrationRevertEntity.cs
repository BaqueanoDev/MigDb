namespace MigDb.Core.Entities;

public class MigrationRevertEntity
{
    public long MigrationRevertId { get; set; }
    public long? MigrationRunId { get; set; }

    public DateTime RevertedAt { get; set; }
    public string RevertedBy { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string KindName => Kind.ToString();
    public MigrationRevertKind Kind { get; set; }
    public MigrationSourceType Source { get; set; }
    public string SourceTypeName => Source.ToString();
    public string? SourceName { get; set; }
    public byte[]? Hash { get; set; }
    public byte[]? PreviousHash { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? TargetRevision { get; set; }
    public string? Detail { get; set; }
}