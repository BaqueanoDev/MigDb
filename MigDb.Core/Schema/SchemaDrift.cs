namespace MigDb.Core.Schema;

public sealed record SchemaDriftItem(SchemaDriftKind Kind, string ObjectType, string Name, IReadOnlyList<string> Details);
