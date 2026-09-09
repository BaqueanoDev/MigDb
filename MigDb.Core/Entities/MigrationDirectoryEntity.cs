namespace MigDb.Core.Entities;

public class MigrationDirectoryEntity
{
    public long MigrationDirectoryId { get; set; }
    public long MigrationRunId { get; set; }
    public string Name { get; set; } = string.Empty;
    public MigrationSourceType Source { get; set; }
    public string? SourceName { get; set; }

    public byte[] Hash { get; set; } = [];
    public int Sequence { get; set; }
    public int RunDuration { get; set; }

    public MigrationRunEntity MigrationRun { get; set; } = default!;
    public List<MigrationFileEntity> Files { get; set; } = [];
}
