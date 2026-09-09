using Dapper;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Options;
using MigDb.Core.Schema;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Infrastructure.Repositories;

/// <summary>
/// Current deployed state of programmable objects (one row per object).
/// The hash-gated deploy reads the last-applied hash from here and upserts it.
/// </summary>
public sealed class SchemaProgrammableRepository(ILogger<SchemaProgrammableRepository> logger, SQLConnectionFactory sqlFactory, DatabaseOptions dbOptions)
{
    public async Task<IReadOnlyList<SchemaProgrammableEntity>> GetAllAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                            ORDER BY {dbOptions.SchemaProgrammableTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);

        IEnumerable<SchemaProgrammableEntity> result = await scope.Connection.QueryAsync<SchemaProgrammableEntity>(cmd);

        return [.. result];
    }

    /// <summary>
    /// Current-state rows for a schema source, used to build the last-applied
    /// hash map the deployer gates re-deploys against.
    /// </summary>
    /// <param name="source">Schema source (Common/Project)</param>
    /// <param name="transaction">Optional transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Programmable rows for the source</returns>
    public async Task<IReadOnlyList<SchemaProgrammableEntity>> GetBySourceAsync(SchemaSourceType source, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                            WHERE Source = @source
                            ORDER BY {dbOptions.SchemaProgrammableTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { source = source.ToString() }, transaction: transaction, cancellationToken: ct);

        IEnumerable<SchemaProgrammableEntity> result = await scope.Connection.QueryAsync<SchemaProgrammableEntity>(cmd);

        return [.. result];
    }

    public async Task<SchemaProgrammableEntity?> GetByIdAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                            WHERE {dbOptions.SchemaProgrammableTableName}Id = @id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        SchemaProgrammableEntity? result = await scope.Connection.QuerySingleOrDefaultAsync<SchemaProgrammableEntity>(cmd);

        return result;
    }

    public async Task<IReadOnlyList<SchemaProgrammableEntity>> GetByIdListAsync(IReadOnlyList<long> ids, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                            WHERE {dbOptions.SchemaProgrammableTableName}Id IN @ids
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { ids }, transaction: transaction, cancellationToken: ct);

        IEnumerable<SchemaProgrammableEntity> result = await scope.Connection.QueryAsync<SchemaProgrammableEntity>(cmd);

        return [.. result];
    }

    /// <summary>
    /// Look up a single programmable by its natural key (schema + name + source)
    /// </summary>
    /// <param name="schema">Object schema</param>
    /// <param name="objectName">Object name</param>
    /// <param name="source">Schema source</param>
    /// <param name="transaction">Optional transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The row, or null when not journaled yet</returns>
    public async Task<SchemaProgrammableEntity?> GetByNameAsync(string schema, string objectName, SchemaSourceType source, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                            WHERE [Schema] = @schema AND ObjectName = @objectName AND Source = @source
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { schema, objectName, source = source.ToString() }, transaction: transaction, cancellationToken: ct);

        SchemaProgrammableEntity? result = await scope.Connection.QuerySingleOrDefaultAsync<SchemaProgrammableEntity>(cmd);

        return result;
    }

    public async Task<long> InsertAsync(SchemaProgrammableEntity programmable, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            INSERT INTO {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                                ([Schema], ObjectName, Type, Source, SourceName, Hash)
                            OUTPUT INSERTED.{dbOptions.SchemaProgrammableTableName}Id
                            VALUES (@Schema, @ObjectName, @Type, @Source, @SourceName, @Hash);
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        var parameters = new
        {
            programmable.Schema,
            programmable.ObjectName,
            Type = programmable.Type.ToString(),
            Source = programmable.Source.ToString(),
            programmable.SourceName,
            programmable.Hash
        };

        CommandDefinition cmd = new(query, parameters, transaction: transaction, cancellationToken: ct);

        long id = await scope.Connection.ExecuteScalarAsync<long>(cmd);

        programmable.SchemaProgrammableId = id;

        logger.LogDebug("Inserted schema programmable {ProgrammableId} ({Programmable})", id, programmable.FullName);

        return id;
    }

    public async Task<byte[]?> UpdateAsync(SchemaProgrammableEntity programmable, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            UPDATE {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                            SET [Schema]                                    = @Schema,
                                ObjectName                                  = @ObjectName,
                                Type                                        = @Type,
                                Source                                      = @Source,
                                SourceName                                  = @SourceName,
                                Hash                                        = @Hash
                            OUTPUT deleted.Hash
                            WHERE {dbOptions.SchemaProgrammableTableName}Id = @SchemaProgrammableId;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        var parameters = new
        {
            programmable.Schema,
            programmable.ObjectName,
            Type = programmable.Type.ToString(),
            Source = programmable.Source.ToString(),
            programmable.SourceName,
            programmable.Hash,
            programmable.SchemaProgrammableId
        };

        CommandDefinition cmd = new(query, parameters, transaction: transaction, cancellationToken: ct);

        byte[]? previousHash = await scope.Connection.ExecuteScalarAsync<byte[]?>(cmd);

        int rows = previousHash is null ? 0 : 1;

        logger.LogDebug("Updated schema programmable {ProgrammableId} ({Programmable}) ({Rows} row(s) affected)", programmable.SchemaProgrammableId, programmable.FullName, rows);

        return previousHash;
    }

    public async Task<bool> DeleteAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            DELETE FROM {dbOptions.SchemaName}.{dbOptions.SchemaProgrammableTableName}
                            WHERE {dbOptions.SchemaProgrammableTableName}Id = @id;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        int rows = await scope.Connection.ExecuteAsync(cmd);

        logger.LogDebug("Deleted schema programmable {ProgrammableId} ({Rows} row(s) affected)", id, rows);

        return rows != 0;
    }
}
