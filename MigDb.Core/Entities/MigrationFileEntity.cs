namespace MigDb.Core.Entities;

public class MigrationFileEntity
{
    public long MigrationFileId { get; set; }
    public long MigrationDirectoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte[] Hash { get; set; } = [];
    public int Sequence { get; set; }
    public int RunDuration { get; set; }

    public MigrationDirectoryEntity MigrationDirectory { get; set; } = default!;
}
