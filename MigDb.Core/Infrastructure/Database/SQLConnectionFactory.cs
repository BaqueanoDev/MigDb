using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace MigDb.Core.Infrastructure.Database;

/// <summary>
/// This class sits in a weird spot and might be worth revisiting at the end
/// 
/// Ideally it would be immutable however due to CLI requiring overriding
/// connection strings ive had to compromise and split it connection into 
/// SQLConnectionScope and this factory itself which generates scope objects
/// using the stored connection string
/// </summary>
public sealed class SQLConnectionFactory
{
    private readonly ILogger<SQLConnectionFactory> _logger;

    public string? ConnectionString { get; set; }

    public SQLConnectionFactory(ILogger<SQLConnectionFactory> logger)
    {
        _logger = logger;
    }

    public SQLConnectionFactory(string connectionString, ILogger<SQLConnectionFactory> logger)
    {
        ConnectionString = connectionString;
        _logger = logger;
    }

    /// <summary>
    /// Create a SQL Connection using the stored connection string
    /// </summary>
    /// <returns>SqlConnection object using stored connection string</returns>
    /// <exception cref="ArgumentException">Connection string cannot be null or white space</exception>
    private SqlConnection Create()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionString, nameof(ConnectionString));

        return new SqlConnection(ConnectionString);
    }

    /// <summary>
    /// Creates SQLConnectionScope that are connection aware
    /// scopes are considered owners if they dont rely on transactions
    /// 
    /// Dont forget to wrap with USING
    /// </summary>
    /// <param name="transaction">Transaction to use for connection</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>A disposable SQLConnectionScope with an opened connection if we are owners</returns>
    public async ValueTask<SQLConnectionLease> CreateScopeAsync(IDbTransaction? transaction, CancellationToken ct = default)
    {
        if (transaction is not null)
        {
            return new SQLConnectionLease((DbConnection)transaction.Connection!, isOwner: false);
        }

        SqlConnection conn = Create();

        try
        {
            await conn.OpenAsync(ct);
        }
        catch (Exception)
        {
            await conn.DisposeAsync();
            throw;
        }

        _logger.LogDebug("Opened SQL connection to {Database} on {Server}", conn.Database, conn.DataSource);

        return new SQLConnectionLease(conn, isOwner: true);
    }
}
