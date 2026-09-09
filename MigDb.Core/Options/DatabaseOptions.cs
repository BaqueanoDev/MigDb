namespace MigDb.Core.Options;

public sealed class DatabaseOptions
{
    public string SchemaName { get; set; } = "migdb";
    public string RunTableName { get; set; } = "MigrationRun";
    public string DirectoryTableName { get; set; } = "MigrationDirectory";
    public string FileTableName { get; set; } = "MigrationFile";
    public string SchemaProgrammableTableName { get; set; } = "SchemaProgrammable";
    public string ProgrammableTableName { get; set; } = "MigrationProgrammable";
    public string RevertTableName { get; set; } = "MigrationRevert";

    /// <summary>
    /// DB command timeout (Default: 10 mins) this might need extending if big data migrations happen 
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Name to use for database lock
    /// </summary>
    public string MigrationLockResource { get; set; } = "MigDb_Migration";

    /// <summary>
    /// How long to wait for the migration app lock before giving up. Probably just leave this as 0
    /// </summary>
    public int MigrationLockTimeoutSeconds { get; set; } = 0;
}
