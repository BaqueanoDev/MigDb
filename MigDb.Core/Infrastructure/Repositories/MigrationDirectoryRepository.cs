using Dapper;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Options;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Infrastructure.Repositories;

public sealed class MigrationDirectoryRepository(ILogger<MigrationDirectoryRepository> logger, SQLConnectionFactory sqlFactory, DatabaseOptions dbOptions)
{
    public async Task<IReadOnlyList<MigrationDirectoryEntity>> GetAllAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            ORDER BY Name
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationDirectoryEntity> result = await scope.Connection.QueryAsync<MigrationDirectoryEntity>(cmd);

        return [.. result];
    }

    public async Task<IReadOnlyList<MigrationDirectoryEntity>> GetAllAsync(MigrationSourceType source, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE Source = @source
                            ORDER BY Name
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { source = source.ToString() }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationDirectoryEntity> result = await scope.Connection.QueryAsync<MigrationDirectoryEntity>(cmd);

        return [.. result];
    }

    public async Task<MigrationDirectoryEntity?> GetByIdAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE {dbOptions.DirectoryTableName}Id = @id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        MigrationDirectoryEntity? result = await scope.Connection.QuerySingleOrDefaultAsync<MigrationDirectoryEntity>(cmd);

        return result;
    }

    public async Task<MigrationDirectoryEntity?> GetByNameAsync(string name, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE Name = @name
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { name }, transaction: transaction, cancellationToken: ct);

        MigrationDirectoryEntity? result = await scope.Connection.QuerySingleOrDefaultAsync<MigrationDirectoryEntity>(cmd);

        return result;
    }

    public async Task<MigrationDirectoryEntity?> GetByNameAsync(string name, MigrationSourceType source, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE Name = @name AND Source = @source
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { name, source = source.ToString() }, transaction: transaction, cancellationToken: ct);

        MigrationDirectoryEntity? result = await scope.Connection.QuerySingleOrDefaultAsync<MigrationDirectoryEntity>(cmd);

        return result;
    }

    public async Task<IReadOnlyList<MigrationDirectoryEntity>> GetByIdListAsync(IReadOnlyList<long> ids, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE {dbOptions.DirectoryTableName}Id IN @ids
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { ids }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationDirectoryEntity> result = await scope.Connection.QueryAsync<MigrationDirectoryEntity>(cmd);

        return [.. result];
    }

    public async Task<MigrationDirectoryEntity?> GetLatestAsync(MigrationSourceType source, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT TOP 1 *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE Source = @source
                            ORDER BY {dbOptions.DirectoryTableName}Id DESC
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { source = source.ToString() }, transaction: transaction, cancellationToken: ct);

        MigrationDirectoryEntity? result = await scope.Connection.QueryFirstOrDefaultAsync<MigrationDirectoryEntity?>(cmd);

        return result;
    }

    public async Task<IReadOnlyList<MigrationDirectoryEntity>> GetByNameListAsync(IReadOnlyList<string> names, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        if (names.Count == 0)
            return [];

        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE Name IN @names
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { names }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationDirectoryEntity> result = await scope.Connection.QueryAsync<MigrationDirectoryEntity>(cmd);

        return [.. result];
    }

    public async Task<IReadOnlyList<MigrationDirectoryEntity>> GetByRunIdAsync(long runId, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE {dbOptions.RunTableName}Id = @runId
                            ORDER BY Sequence
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { runId }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationDirectoryEntity> result = await scope.Connection.QueryAsync<MigrationDirectoryEntity>(cmd);

        return [.. result];
    }

    public async Task<long> InsertAsync(MigrationDirectoryEntity directory, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            INSERT INTO {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                                ({dbOptions.RunTableName}Id, Name, Source, SourceName, Hash, Sequence, RunDuration)
                            OUTPUT INSERTED.{dbOptions.DirectoryTableName}Id
                            VALUES (@MigrationRunId, @Name, @Source, @SourceName, @Hash, @Sequence, @RunDuration);
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        var parameters = new
        {
            directory.MigrationRunId,
            directory.Name,
            Source = directory.Source.ToString(),
            directory.SourceName,
            directory.Hash,
            directory.Sequence,
            directory.RunDuration
        };

        CommandDefinition cmd = new(query, parameters, transaction: transaction, cancellationToken: ct);

        long id = await scope.Connection.ExecuteScalarAsync<long>(cmd);

        directory.MigrationDirectoryId = id;

        logger.LogDebug("Inserted migration directory {DirectoryId} ({Directory}, run {RunId})", id, directory.Name, directory.MigrationRunId);

        return id;
    }

    public async Task<bool> UpdateAsync(MigrationDirectoryEntity directory, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            UPDATE {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            SET {dbOptions.RunTableName}Id = @MigrationRunId,
                                Name                                = @Name,
                                Source                              = @Source,
                                SourceName                          = @SourceName,
                                Hash                                = @Hash,
                                Sequence                            = @Sequence,
                                RunDuration                         = @RunDuration
                            WHERE {dbOptions.DirectoryTableName}Id = @MigrationDirectoryId;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        var parameters = new
        {
            directory.MigrationRunId,
            directory.Name,
            Source = directory.Source.ToString(),
            directory.SourceName,
            directory.Hash,
            directory.Sequence,
            directory.RunDuration,
            directory.MigrationDirectoryId
        };

        CommandDefinition cmd = new(query, parameters, transaction: transaction, cancellationToken: ct);

        int rows = await scope.Connection.ExecuteAsync(cmd);

        logger.LogDebug("Updated migration directory {DirectoryId} ({Directory}) ({Rows} row(s) affected)", directory.MigrationDirectoryId, directory.Name, rows);

        return rows != 0;
    }

    public async Task<bool> DeleteAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            DELETE FROM {dbOptions.SchemaName}.{dbOptions.DirectoryTableName}
                            WHERE {dbOptions.DirectoryTableName}Id = @id;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        int rows = await scope.Connection.ExecuteAsync(cmd);

        logger.LogDebug("Deleted migration directory {DirectoryId} ({Rows} row(s) affected)", id, rows);

        return rows != 0;
    }
}
