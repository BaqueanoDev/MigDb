namespace MigDb.Core.Schema;

public enum SchemaDriftKind
{
    MissingFromDatabase,
    MissingFromProject,
    Different,
}
