namespace MigDb.Core.Entities;

public class MigrationProgrammableEntity
{
    public long MigrationProgrammableId { get; set; }
    public long SchemaProgrammableId { get; set; }
    public long MigrationRunId { get; set; }

    public byte[]? HashBefore { get; set; }
    public byte[] HashAfter { get; set; } = [];

    public int RunDuration { get; set; }

    public SchemaProgrammableEntity SchemaProgrammable { get; set; } = default!;
    public MigrationRunEntity MigrationRun { get; set; } = default!;
}
