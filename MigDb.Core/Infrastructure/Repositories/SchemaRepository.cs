using Dapper;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Options;
using MigDb.Core.Schema.Programmable;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Infrastructure.Repositories;

public sealed class SchemaRepository(ILogger<SchemaRepository> logger, SQLConnectionFactory sqlFactory, DatabaseOptions dbOptions)
{
    public async Task<bool> SchemaExistAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        const string query = "SELECT CAST(COUNT(*) AS BIT) FROM sys.schemas WHERE name = @schemaName";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(
            query,
            new { schemaName = dbOptions.SchemaName },
            transaction: transaction,
            cancellationToken: ct);

        bool result = await scope.Connection.QuerySingleOrDefaultAsync<bool>(cmd);

        logger.LogDebug("Checking schema exists: {Exists}", result);

        return result;
    }

    public async Task<IReadOnlyList<SchemaProgrammableBinding>> GetUnEditableProgrammablesAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        const string query = @"
                SELECT DISTINCT
                    OBJECT_SCHEMA_NAME(d.referenced_id) AS [Schema],
                    OBJECT_NAME(d.referenced_id)        AS ObjectName,
                    'SchemaBound'                       AS Reason
                FROM sys.sql_expression_dependencies AS d
                    INNER JOIN sys.objects AS o ON o.object_id = d.referenced_id
                WHERE d.is_schema_bound_reference = 1
                  AND d.referenced_database_name IS NULL
                  AND o.type IN ('FN', 'IF', 'TF', 'V', 'P', 'TR')

                UNION

                SELECT
                    OBJECT_SCHEMA_NAME(v.object_id) AS [Schema],
                    OBJECT_NAME(v.object_id)        AS ObjectName,
                    'IndexedView'                   AS Reason
                FROM sys.views AS v
                WHERE EXISTS (SELECT 1
                              FROM sys.indexes AS i
                              WHERE i.object_id = v.object_id AND i.index_id > 0)";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);

        IEnumerable<SchemaProgrammableBinding> result = await scope.Connection.QueryAsync<SchemaProgrammableBinding>(cmd);

        List<SchemaProgrammableBinding> bindings = [.. result];

        logger.LogDebug("Found {Count} object(s) the database will not let us ALTER", bindings.Count);

        return bindings;
    }

    private async Task<bool> TableExistAsync(string tableName, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        const string query = @"SELECT CAST(COUNT(*) AS BIT)
                               FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_NAME = @tableName AND TABLE_SCHEMA = @schemaName";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(
            query,
            new { tableName, schemaName = dbOptions.SchemaName },
            transaction: transaction,
            cancellationToken: ct);

        bool result = await scope.Connection.QuerySingleOrDefaultAsync<bool>(cmd);

        logger.LogDebug("Checking table {Table} exists: {Exists}", tableName, result);

        return result;
    }

    public async Task<bool> MigrationRunTableExistsAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        return await TableExistAsync(dbOptions.RunTableName, transaction, ct);
    }

    public async Task<bool> MigrationDirectoryTableExistsAsync(IDbTransaction? transaction = null, CancellationToken ct = default)

    {
        return await TableExistAsync(dbOptions.DirectoryTableName, transaction, ct);
    }

    public async Task<bool> MigrationFileTableExistsAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        return await TableExistAsync(dbOptions.FileTableName, transaction, ct);
    }

    public async Task<bool> SchemaProgrammableTableExistsAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        return await TableExistAsync(dbOptions.SchemaProgrammableTableName, transaction, ct);
    }

    public async Task<bool> MigrationProgrammableTableExistsAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        return await TableExistAsync(dbOptions.ProgrammableTableName, transaction, ct);
    }

    public async Task<bool> MigrationRevertTableExistsAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        return await TableExistAsync(dbOptions.RevertTableName, transaction, ct);
    }

    public async Task<bool> MigrationSchemaInitialisedAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        bool r = await MigrationRunTableExistsAsync(transaction, ct);
        bool d = await MigrationDirectoryTableExistsAsync(transaction, ct);
        bool f = await MigrationFileTableExistsAsync(transaction, ct);
        bool sp = await SchemaProgrammableTableExistsAsync(transaction, ct);
        bool p = await MigrationProgrammableTableExistsAsync(transaction, ct);
        bool rv = await MigrationRevertTableExistsAsync(transaction, ct);

        return r && d && f && sp && p && rv;
    }

    public async Task CreateSchemaAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"EXEC ('CREATE SCHEMA {dbOptions.SchemaName}');";

        logger.LogInformation("Creating schema {Schema}", dbOptions.SchemaName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }
    public async Task CreateMigrationRunTableAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            CREATE TABLE {dbOptions.SchemaName}.{dbOptions.RunTableName}
                            (
                                {dbOptions.RunTableName}Id     BIGINT                IDENTITY(1,1) NOT NULL,
                                StartTime                               DATETIME2(7)          NOT NULL,
                                CompleteTime                            DATETIME2(7)          NOT NULL,
                                RunDuration                             INT                   NOT NULL,
                                RunResult                               NVARCHAR(20)          NOT NULL,
                                RunResultMessage                        NVARCHAR(MAX)         NOT NULL,
                                AppliedBy                               NVARCHAR(256)         NOT NULL,

                                CONSTRAINT PK_{dbOptions.RunTableName}_{dbOptions.RunTableName}Id PRIMARY KEY ({dbOptions.RunTableName}Id)
                            );
                        ";

        logger.LogInformation("Creating table {Schema}.{Table}", dbOptions.SchemaName, dbOptions.RunTableName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }

    public async Task CreateMigrationDirectoryTableAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            CREATE TABLE {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            (
                                {dbOptions.DirectoryTableName}Id    BIGINT            IDENTITY(1,1) NOT NULL,
                                {dbOptions.RunTableName}Id       BIGINT            NOT NULL,
                                Name                                      NVARCHAR(500)     NOT NULL,
                                Source                                    NVARCHAR(20)      NOT NULL,
                                SourceName                                NVARCHAR(256)     NULL,
                                Hash                                      BINARY(32)        NOT NULL,
                                Sequence                                  INT               NOT NULL,
                                RunDuration                               INT               NOT NULL,

                                CONSTRAINT PK_{dbOptions.DirectoryTableName}_{dbOptions.DirectoryTableName}Id PRIMARY KEY ({dbOptions.DirectoryTableName}Id),
                                CONSTRAINT FK_{dbOptions.DirectoryTableName}_{dbOptions.RunTableName}Id    FOREIGN KEY ({dbOptions.RunTableName}Id) REFERENCES {dbOptions.SchemaName}.{dbOptions.RunTableName} ({dbOptions.RunTableName}Id),
                                CONSTRAINT UQ_{dbOptions.DirectoryTableName}_Name_Source                            UNIQUE (Name, Source)
                            );
                        ";

        logger.LogInformation("Creating table {Schema}.{Table}", dbOptions.SchemaName, dbOptions.DirectoryTableName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }

    public async Task CreateMigrationFileTableAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            CREATE TABLE {dbOptions.SchemaName}.{dbOptions.FileTableName}
                            (
                                {dbOptions.FileTableName}Id     BIGINT          IDENTITY(1,1) NOT NULL,
                                {dbOptions.DirectoryTableName}Id   BIGINT          NOT NULL,
                                Name                                     NVARCHAR(260)   NOT NULL,
                                Hash                                     BINARY(32)      NOT NULL,
                                Sequence                                 INT             NOT NULL,
                                RunDuration                              INT             NOT NULL,

                                CONSTRAINT PK_{dbOptions.FileTableName}_{dbOptions.FileTableName}Id     PRIMARY KEY ({dbOptions.FileTableName}Id),
                                CONSTRAINT FK_{dbOptions.FileTableName}_{dbOptions.DirectoryTableName}Id   FOREIGN KEY ({dbOptions.DirectoryTableName}Id) REFERENCES {dbOptions.SchemaName}.{dbOptions.DirectoryTableName} ({dbOptions.DirectoryTableName}Id)
                            );
                        ";

        logger.LogInformation("Creating table {Schema}.{Table}", dbOptions.SchemaName, dbOptions.FileTableName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }

    public async Task CreateSchemaProgrammableTableAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            CREATE TABLE {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                            (
                                {dbOptions.SchemaProgrammableTableName}Id   BIGINT          IDENTITY(1,1) NOT NULL,
                                [Schema]                                           NVARCHAR(128)   NOT NULL,
                                ObjectName                                         NVARCHAR(256)   NOT NULL,
                                Type                                               NVARCHAR(20)    NOT NULL,
                                Source                                             NVARCHAR(20)    NOT NULL,
                                SourceName                                         NVARCHAR(256)   NULL,
                                Hash                                               BINARY(32)      NOT NULL,

                                CONSTRAINT PK_{dbOptions.SchemaProgrammableTableName}_{dbOptions.SchemaProgrammableTableName}Id   PRIMARY KEY ({dbOptions.SchemaProgrammableTableName}Id),
                                CONSTRAINT UQ_{dbOptions.SchemaProgrammableTableName}_Schema_ObjectName_Source                    UNIQUE ([Schema], ObjectName, Source)
                            );
                        ";

        logger.LogInformation("Creating table {Schema}.{Table}", dbOptions.SchemaName, dbOptions.SchemaProgrammableTableName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }

    public async Task CreateMigrationProgrammableTableAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            CREATE TABLE {dbOptions.SchemaName}.{dbOptions.ProgrammableTableName}
                            (
                                {dbOptions.ProgrammableTableName}Id   BIGINT          IDENTITY(1,1) NOT NULL,
                                {dbOptions.SchemaProgrammableTableName}Id   BIGINT    NOT NULL,
                                {dbOptions.RunTableName}Id            BIGINT          NOT NULL,
                                HashBefore                                   BINARY(32)      NULL,
                                HashAfter                                    BINARY(32)      NOT NULL,
                                RunDuration                                  INT             NOT NULL,

                                CONSTRAINT PK_{dbOptions.ProgrammableTableName}_{dbOptions.ProgrammableTableName}Id       PRIMARY KEY ({dbOptions.ProgrammableTableName}Id),
                                CONSTRAINT FK_{dbOptions.ProgrammableTableName}_{dbOptions.SchemaProgrammableTableName}Id FOREIGN KEY ({dbOptions.SchemaProgrammableTableName}Id) REFERENCES {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName} ({dbOptions.SchemaProgrammableTableName}Id),
                                CONSTRAINT FK_{dbOptions.ProgrammableTableName}_{dbOptions.RunTableName}Id                FOREIGN KEY ({dbOptions.RunTableName}Id) REFERENCES {dbOptions.SchemaName}.{dbOptions.RunTableName} ({dbOptions.RunTableName}Id)
                            );
                        ";

        logger.LogInformation("Creating table {Schema}.{Table}", dbOptions.SchemaName, dbOptions.ProgrammableTableName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }

    public async Task CreateMigrationRevertTableAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            CREATE TABLE {dbOptions.SchemaName}.{dbOptions.RevertTableName}
                            (
                                {dbOptions.RevertTableName}Id         BIGINT          IDENTITY(1,1) NOT NULL,
                                RevertedAt                                   DATETIME2(7)    NOT NULL,
                                RevertedBy                                   NVARCHAR(256)   NOT NULL,
                                Kind                                         NVARCHAR(20)    NOT NULL,
                                Name                                         NVARCHAR(500)   NOT NULL,
                                Source                                       NVARCHAR(20)    NOT NULL,
                                SourceName                                   NVARCHAR(256)   NULL,
                                Hash                                         BINARY(32)      NULL,
                                PreviousHash                                 BINARY(32)      NULL,
                                {dbOptions.RunTableName}Id            BIGINT          NULL,
                                AppliedAt                                    DATETIME2(7)    NULL,
                                TargetRevision                               NVARCHAR(200)   NULL,
                                Detail                                       NVARCHAR(MAX)   NULL,

                                CONSTRAINT PK_{dbOptions.RevertTableName}_{dbOptions.RevertTableName}Id PRIMARY KEY ({dbOptions.RevertTableName}Id)
                            );
                        ";

        logger.LogInformation("Creating table {Schema}.{Table}", dbOptions.SchemaName, dbOptions.RevertTableName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }

    public async Task CreateSchemaTablesAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        await CreateMigrationRunTableAsync(transaction, ct);
        await CreateMigrationDirectoryTableAsync(transaction, ct);
        await CreateMigrationFileTableAsync(transaction, ct);
        await CreateSchemaProgrammableTableAsync(transaction, ct);
        await CreateMigrationProgrammableTableAsync(transaction, ct);
        await CreateMigrationRevertTableAsync(transaction, ct);
    }

    public async Task DropSchemaAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $"DROP SCHEMA IF EXISTS {dbOptions.SchemaName};";

        logger.LogWarning("Dropping schema {Schema}", dbOptions.SchemaName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }

    public async Task ResetSchemaAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        logger.LogWarning("Resetting migration schema and tables in {Schema}", dbOptions.SchemaName);

        await DropSchemaTablesAsync(transaction, ct);
        await DropSchemaAsync(transaction, ct);
        await CreateSchemaAsync(transaction, ct);
        await CreateSchemaTablesAsync(transaction, ct);
    }

    public async Task DropSchemaTablesAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            DROP TABLE IF EXISTS {dbOptions.SchemaName}.{dbOptions.RevertTableName};
                            DROP TABLE IF EXISTS {dbOptions.SchemaName}.{dbOptions.FileTableName};
                            DROP TABLE IF EXISTS {dbOptions.SchemaName}.{dbOptions.DirectoryTableName};
                            DROP TABLE IF EXISTS {dbOptions.SchemaName}.{dbOptions.ProgrammableTableName};
                            DROP TABLE IF EXISTS {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName};
                            DROP TABLE IF EXISTS {dbOptions.SchemaName}.{dbOptions.RunTableName};
                        ";

        logger.LogWarning("Dropping schema tables in {Schema}", dbOptions.SchemaName);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);
        await scope.Connection.ExecuteAsync(cmd);
    }

    public async Task ResetSchemaTablesAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        logger.LogWarning("Resetting schema tables in {Schema}", dbOptions.SchemaName);

        await DropSchemaTablesAsync(transaction, ct);
        await CreateSchemaTablesAsync(transaction, ct);
    }

    /// <summary>
    /// Aquires an exclusive sp_getapplock from DB for migration operations
    /// Lock is automatically released on commit/rollback
    /// </summary>
    /// <returns>The sp_getapplock <see href="https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver17#return-code-values" >result code</see></returns>
    public async Task<int> AcquireMigrationLockAsync(IDbTransaction transaction, CancellationToken ct = default)
    {
        DynamicParameters parameters = new();
        parameters.Add("@Resource", dbOptions.MigrationLockResource);
        parameters.Add("@LockMode", "Exclusive");
        parameters.Add("@LockOwner", "Transaction");
        parameters.Add("@LockTimeout", dbOptions.MigrationLockTimeoutSeconds * 1000);
        parameters.Add("@Result", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(
            "sys.sp_getapplock",
            parameters,
            transaction: transaction,
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct);

        await scope.Connection.ExecuteAsync(cmd);

        int result = parameters.Get<int>("@Result");

        if (result < 0)
            logger.LogWarning("Failed to acquire migration lock {Resource} (result {Result})", dbOptions.MigrationLockResource, result);
        else
            logger.LogDebug("Acquired migration lock {Resource} (result {Result})", dbOptions.MigrationLockResource, result);

        return result;
    }

    public async Task EnsureSchemaExistAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        if (await SchemaExistAsync(transaction, ct))
        {
            logger.LogDebug("Schema {Schema} already exists, skipping create", dbOptions.SchemaName);
            return;
        }

        await CreateSchemaAsync(transaction, ct);
    }

    public async Task EnsureSchemaTablesExistAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        if (!await MigrationRunTableExistsAsync(transaction, ct))
            await CreateMigrationRunTableAsync(transaction, ct);

        if (!await MigrationDirectoryTableExistsAsync(transaction, ct))
            await CreateMigrationDirectoryTableAsync(transaction, ct);

        if (!await MigrationFileTableExistsAsync(transaction, ct))
            await CreateMigrationFileTableAsync(transaction, ct);

        if (!await SchemaProgrammableTableExistsAsync(transaction, ct))
            await CreateSchemaProgrammableTableAsync(transaction, ct);

        if (!await MigrationProgrammableTableExistsAsync(transaction, ct))
            await CreateMigrationProgrammableTableAsync(transaction, ct);

        if (!await MigrationRevertTableExistsAsync(transaction, ct))
            await CreateMigrationRevertTableAsync(transaction, ct);
    }
}
