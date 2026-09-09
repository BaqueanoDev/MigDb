namespace MigDb.Core.Schema.Programmable;

public sealed record SchemaProgrammableBinding(string Schema, string ObjectName, SchemaProgrammableExclusion Reason)
{
    public string FullName => $"{Schema}.{ObjectName}";
}
