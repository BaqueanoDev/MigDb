using Dapper;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Options;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Infrastructure.Repositories;

public sealed class MigrationFileRepository(ILogger<MigrationFileRepository> logger, SQLConnectionFactory sqlFactory, DatabaseOptions dbOptions)
{
    public async Task<IReadOnlyList<MigrationFileEntity>> GetAllAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.FileTableName}
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationFileEntity> result = await scope.Connection.QueryAsync<MigrationFileEntity>(cmd);

        return [.. result];
    }

    public async Task<MigrationFileEntity?> GetByIdAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.FileTableName}
                            WHERE {dbOptions.FileTableName}Id = @id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        MigrationFileEntity? result = await scope.Connection.QuerySingleOrDefaultAsync<MigrationFileEntity>(cmd);

        return result;
    }

    public async Task<IReadOnlyList<MigrationFileEntity>> GetByIdListAsync(IReadOnlyList<long> ids, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.FileTableName}
                            WHERE {dbOptions.FileTableName}Id IN @ids
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { ids }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationFileEntity> result = await scope.Connection.QueryAsync<MigrationFileEntity>(cmd);

        return [.. result];
    }

    public async Task<IReadOnlyList<MigrationFileEntity>> GetByDirectoryIdAsync(long directoryId, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.FileTableName}
                            WHERE {dbOptions.DirectoryTableName}Id = @directoryId
                            ORDER BY Sequence
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { directoryId }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationFileEntity> result = await scope.Connection.QueryAsync<MigrationFileEntity>(cmd);

        return [.. result];
    }

    public async Task<IReadOnlyList<MigrationFileEntity>> GetByDirectoryIdListAsync(IReadOnlyList<long> directoryIds, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.FileTableName}
                            WHERE {dbOptions.DirectoryTableName}Id in @directoryIds
                            ORDER BY Sequence
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { directoryIds }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationFileEntity> result = await scope.Connection.QueryAsync<MigrationFileEntity>(cmd);

        return [.. result];
    }

    public async Task<long> InsertAsync(MigrationFileEntity file, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            INSERT INTO {dbOptions.SchemaName}.{dbOptions.FileTableName}
                                ({dbOptions.DirectoryTableName}Id, Name, Hash, Sequence, RunDuration)
                            OUTPUT INSERTED.{dbOptions.FileTableName}Id
                            VALUES (@MigrationDirectoryId, @Name, @Hash, @Sequence, @RunDuration);
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, file, transaction: transaction, cancellationToken: ct);

        long id = await scope.Connection.ExecuteScalarAsync<long>(cmd);

        file.MigrationFileId = id;

        logger.LogDebug("Inserted migration file {FileId} ({File}, directory {DirectoryId})", id, file.Name, file.MigrationDirectoryId);

        return id;
    }

    public async Task<bool> UpdateAsync(MigrationFileEntity file, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            UPDATE {dbOptions.SchemaName}.{dbOptions.FileTableName}
                            SET {dbOptions.DirectoryTableName}Id = @MigrationDirectoryId,
                                Name                          = @Name,
                                Hash                          = @Hash,
                                Sequence                      = @Sequence,
                                RunDuration                   = @RunDuration
                            WHERE {dbOptions.FileTableName}Id = @MigrationFileId;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, file, transaction: transaction, cancellationToken: ct);

        int rows = await scope.Connection.ExecuteAsync(cmd);

        logger.LogDebug("Updated migration file {FileId} ({File}) ({Rows} row(s) affected)", file.MigrationFileId, file.Name, rows);

        return rows != 0;
    }

    public async Task<bool> DeleteAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            DELETE FROM {dbOptions.SchemaName}.{dbOptions.FileTableName}
                            WHERE {dbOptions.FileTableName}Id = @id;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        int rows = await scope.Connection.ExecuteAsync(cmd);

        logger.LogDebug("Deleted migration file {FileId} ({Rows} row(s) affected)", id, rows);

        return rows != 0;
    }
}
