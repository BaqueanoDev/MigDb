namespace MigDb.Core.Schema.Programmable;

public sealed record SchemaProgrammableFile(
    string Schema,
    string ObjectName,
    SchemaSource Source,
    SchemaObjectKind Type,
    FileInfo FileInfo,
    string Script,
    byte[] Hash,
    SchemaProgrammableExclusion Exclusion = SchemaProgrammableExclusion.None)
{
    public bool Excluded => Exclusion != SchemaProgrammableExclusion.None;

    public string FullName => $"{Schema}.{ObjectName}";
    public SchemaSourceType SourceType => SchemaSource.ToSchemaSourceType(Source);
    public string? SourceName => SchemaSource.ToSourceName(Source);
}
