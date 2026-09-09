using Dapper;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Options;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Infrastructure.Repositories;

/// <summary>
/// Per-run journal of programmable deploys: one append-only row each time a
/// <see cref="SchemaProgrammableEntity"/> is (re)deployed in a run, recording
/// the hash it carried before, the hash that was applied and how long it took.
/// </summary>
public sealed class MigrationProgrammableRepository(ILogger<MigrationProgrammableRepository> logger, SQLConnectionFactory sqlFactory, DatabaseOptions dbOptions)
{
    public async Task<IReadOnlyList<MigrationProgrammableEntity>> GetAllAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.ProgrammableTableName}
                            ORDER BY {dbOptions.ProgrammableTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationProgrammableEntity> result = await scope.Connection.QueryAsync<MigrationProgrammableEntity>(cmd);

        return [.. result];
    }

    public async Task<MigrationProgrammableEntity?> GetByIdAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.ProgrammableTableName}
                            WHERE {dbOptions.ProgrammableTableName}Id = @id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        MigrationProgrammableEntity? result = await scope.Connection.QuerySingleOrDefaultAsync<MigrationProgrammableEntity>(cmd);

        return result;
    }

    /// <summary>
    /// All programmable deploy rows written by a single run.
    /// </summary>
    /// <param name="runId">Run id</param>
    /// <param name="transaction">Optional transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Deploy rows for the run</returns>
    public async Task<IReadOnlyList<MigrationProgrammableEntity>> GetByRunIdAsync(long runId, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.ProgrammableTableName}
                            WHERE {dbOptions.RunTableName}Id = @runId
                            ORDER BY {dbOptions.ProgrammableTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { runId }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationProgrammableEntity> result = await scope.Connection.QueryAsync<MigrationProgrammableEntity>(cmd);

        return [.. result];
    }

    /// <summary>
    /// Deploy history for a single programmable object, newest first.
    /// </summary>
    /// <param name="schemaProgrammableId">Schema programmable id</param>
    /// <param name="transaction">Optional transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Deploy rows for the object</returns>
    public async Task<IReadOnlyList<MigrationProgrammableEntity>> GetBySchemaProgrammableIdAsync(long schemaProgrammableId, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.ProgrammableTableName}
                            WHERE {dbOptions.SchemaProgrammableTableName}Id = @schemaProgrammableId
                            ORDER BY {dbOptions.ProgrammableTableName}Id DESC
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { schemaProgrammableId }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationProgrammableEntity> result = await scope.Connection.QueryAsync<MigrationProgrammableEntity>(cmd);

        return [.. result];
    }

    public async Task<long> InsertAsync(MigrationProgrammableEntity programmable, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            INSERT INTO {dbOptions.SchemaName}.{dbOptions.ProgrammableTableName}
                                ({dbOptions.SchemaProgrammableTableName}Id, {dbOptions.RunTableName}Id, HashBefore, HashAfter, RunDuration)
                            OUTPUT INSERTED.{dbOptions.ProgrammableTableName}Id
                            VALUES (@SchemaProgrammableId, @MigrationRunId, @HashBefore, @HashAfter, @RunDuration);
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, programmable, transaction: transaction, cancellationToken: ct);

        long id = await scope.Connection.ExecuteScalarAsync<long>(cmd);

        programmable.MigrationProgrammableId = id;

        logger.LogDebug("Inserted programmable deploy {ProgrammableId} (schema programmable {SchemaProgrammableId}, run {RunId})", id, programmable.SchemaProgrammableId, programmable.MigrationRunId);

        return id;
    }

    public async Task<bool> DeleteAsync(long id, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            DELETE FROM {dbOptions.SchemaName}.{dbOptions.ProgrammableTableName}
                            WHERE {dbOptions.ProgrammableTableName}Id = @id;
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { id }, transaction: transaction, cancellationToken: ct);

        int rows = await scope.Connection.ExecuteAsync(cmd);

        logger.LogDebug("Deleted programmable deploy {ProgrammableId} ({Rows} row(s) affected)", id, rows);

        return rows != 0;
    }
}
