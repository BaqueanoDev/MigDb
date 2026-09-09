namespace MigDb.Core.Schema;

public sealed record SchemaDriftReport(string Database, IReadOnlyList<SchemaDriftItem> Items)
{
    public bool HasDrift => Items.Count > 0;

    public IEnumerable<SchemaDriftItem> OfKind(SchemaDriftKind kind) => Items.Where(i => i.Kind == kind);
}
