namespace MigDb.Core.Migration;

/// <summary>
/// Thrown when a interacting witha database with no schema or tables
/// </summary>
public sealed class MigrationSchemaNotInitialisedException()
    : Exception("Migration schema/tables are missing in the target database. Run 'schema journal init' first.");