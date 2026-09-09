using MigDb.Core.Schema;

namespace MigDb.Core.Entities;

public class SchemaProgrammableEntity
{
    public long SchemaProgrammableId { get; set; }
    public string Schema { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string FullName => $"{Schema}.{ObjectName}";
    public SchemaObjectKind Type { get; set; }
    public SchemaSourceType Source { get; set; }
    public string? SourceName { get; set; }

    public byte[] Hash { get; set; } = [];

    public List<MigrationProgrammableEntity> Migrations { get; set; } = [];
}
