using Dapper;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Options;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Infrastructure.Repositories;

public sealed class MigrationRevertRepository(ILogger<MigrationRevertRepository> logger, SQLConnectionFactory sqlFactory, DatabaseOptions dbOptions)
{
    public async Task<IReadOnlyList<MigrationRevertEntity>> GetAllAsync(IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.RevertTableName}
                            ORDER BY {dbOptions.RevertTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationRevertEntity> result = await scope.Connection.QueryAsync<MigrationRevertEntity>(cmd);

        return [.. result];
    }

    public async Task<IReadOnlyList<MigrationRevertEntity>> GetBySourceAsync(MigrationSourceType source, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.RevertTableName}
                            WHERE Source = @source
                            ORDER BY {dbOptions.RevertTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { source = source.ToString() }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationRevertEntity> result = await scope.Connection.QueryAsync<MigrationRevertEntity>(cmd);

        return [.. result];
    }

    public async Task<IReadOnlyList<MigrationRevertEntity>> GetByNameAsync(string name, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT *
                            FROM {dbOptions.SchemaName}.{dbOptions.RevertTableName}
                            WHERE Name = @name
                            ORDER BY {dbOptions.RevertTableName}Id
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { name }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationRevertEntity> result = await scope.Connection.QueryAsync<MigrationRevertEntity>(cmd);

        return [.. result];
    }

    public async Task<IReadOnlyList<MigrationRevertEntity>> GetLatestAsync(int n = 50, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            SELECT TOP (@n) *
                            FROM {dbOptions.SchemaName}.{dbOptions.RevertTableName}
                            ORDER BY {dbOptions.RevertTableName}Id DESC
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, new { n }, transaction: transaction, cancellationToken: ct);

        IEnumerable<MigrationRevertEntity> result = await scope.Connection.QueryAsync<MigrationRevertEntity>(cmd);

        return [.. result];
    }

    public async Task<long> InsertAsync(MigrationRevertEntity revert, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        string query = $@"
                            INSERT INTO {dbOptions.SchemaName}.{dbOptions.RevertTableName}
                                (RevertedAt, RevertedBy, Kind, Name, Source, SourceName, Hash, PreviousHash, {dbOptions.RunTableName}Id, AppliedAt, TargetRevision, Detail)
                            OUTPUT INSERTED.{dbOptions.RevertTableName}Id
                            VALUES (@RevertedAt, @RevertedBy, @KindName, @Name, @SourceTypeName, @SourceName, @Hash, @PreviousHash, @MigrationRunId, @AppliedAt, @TargetRevision, @Detail);
                         ";

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction, ct);

        CommandDefinition cmd = new(query, revert, transaction: transaction, cancellationToken: ct);

        long id = await scope.Connection.ExecuteScalarAsync<long>(cmd);

        revert.MigrationRevertId = id;

        logger.LogDebug("Recorded revert {RevertId} ({Kind} {Name})", id, revert.Kind, revert.Name);

        return id;
    }
}
