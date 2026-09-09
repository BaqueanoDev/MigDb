namespace MigDb.Core.Schema;

/// <summary>
/// The object a generated DDL batch targets, together with its kind.
/// For <see cref="SchemaObjectKind.Schema"/> there is no containing schema so
/// <see cref="Schema"/> is empty.
/// </summary>
/// <param name="Schema">Containing schema (empty for a schema object)</param>
/// <param name="Name">Object name</param>
/// <param name="Kind">Object kind</param>
public sealed record SchemaObject(string Schema, string Name, SchemaObjectKind Kind)
{
    public string FullName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}
