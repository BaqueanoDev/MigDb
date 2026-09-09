using Dapper;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Options;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Infrastructure.Repositories;

public sealed class MigrationRunRepository(ILogger<MigrationRunRepository> logger, SQLConnectionFactory sqlFactory, DatabaseOptions dbOptions)
{
    public async Task<IReadOnlyList<MigrationRunEntity>> GetAllAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.RunTableName}
                            ORDER BY {dbOptions.RunTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationRunEntity> result = await scope.Connection.QueryAsync<MigrationRunEntity>(cmd);

        return [.. result];
    }

    public async Task<MigrationRunEntity?> GetByIdAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.RunTableName}
                            WHERE {dbOptions.RunTableName}Id = @id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        MigrationRunEntity? result = await scope.Connection.QuerySingleOrDefaultAsync<MigrationRunEntity>(cmd);

        return result;
    }

    public async Task<IReadOnlyList<MigrationRunEntity>> GetByIdListAsync(IReadOnlyList<long> ids, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.RunTableName}
                            WHERE {dbOptions.RunTableName}Id IN @ids
                            ORDER BY {dbOptions.RunTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { ids }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationRunEntity> result = await scope.Connection.QueryAsync<MigrationRunEntity>(cmd);

        return [.. result];
    }

    public async Task<IReadOnlyList<MigrationRunEntity>> GetLatestAsync(int n = 1, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT TOP (@n) *
                            FROM {dbOptions.SchemaName}.{dbOptions.RunTableName}
                            ORDER BY {dbOptions.RunTableName}Id DESC
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { n }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationRunEntity> result = await scope.Connection.QueryAsync<MigrationRunEntity>(cmd);

        return [.. result];
    }

    public async Task<long> InsertAsync(MigrationRunEntity run, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            INSERT INTO {dbOptions.SchemaName}.{dbOptions.RunTableName}
                                (StartTime, CompleteTime, RunDuration, RunResult, RunResultMessage, AppliedBy)
                            OUTPUT INSERTED.{dbOptions.RunTableName}Id
                            VALUES (@StartTime, @CompleteTime, @RunDuration, @RunResultName, @RunResultMessage, @AppliedBy);
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, run, transaction: transaction, cancellationToken: ct);

        long id = await scope.Connection.ExecuteScalarAsync<long>(cmd);

        run.MigrationRunId = id;

        logger.LogDebug("Inserted migration run {RunId} (applied by {AppliedBy})", id, run.AppliedBy);

        return id;
    }

    public async Task<bool> UpdateAsync(MigrationRunEntity run, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            UPDATE {dbOptions.SchemaName}.{dbOptions.RunTableName}
                            SET
                                StartTime        = @StartTime,
                                CompleteTime     = @CompleteTime,
                                RunDuration      = @RunDuration,
                                RunResult        = @RunResultName,
                                RunResultMessage = @RunResultMessage,
                                AppliedBy        = @AppliedBy
                            WHERE {dbOptions.RunTableName}Id = @MigrationRunId;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, run, transaction: transaction, cancellationToken: ct);

        int rows = await scope.Connection.ExecuteAsync(cmd);

        logger.LogDebug("Updated migration run {RunId} ({Rows} row(s) affected)", run.MigrationRunId, rows);

        return rows != 0;
    }

    public async Task<bool> DeleteAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            DELETE FROM {dbOptions.SchemaName}.{dbOptions.RunTableName}
                            WHERE {dbOptions.RunTableName}Id = @id;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        int rows = await scope.Connection.ExecuteAsync(cmd);

        logger.LogDebug("Deleted migration run {RunId} ({Rows} row(s) affected)", id, rows);

        return rows != 0;
    }
}
