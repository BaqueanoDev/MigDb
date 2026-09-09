using System.Data.Common;

namespace MigDb.Core.Infrastructure.Database;

/// <summary>
/// An owner aware DbConnection
/// 
/// Why? well repos need to be able to support both scenarios
/// 1. Transaction aware but not connection owners 
/// 2. Connection owners without transaction or default transaction
/// 
/// Essentially sometimes we need to pass transactions around and make sure connections arent disposed / closed prematurely
/// 
/// Do i like this? no. Is there a better way to do this? probs. 
/// TODO: maybe ask an LLM
/// </summary>
/// <param name="connection">Database connection</param>
/// <param name="isOwner">If true, we dispose the connection else assumes disposed somewhere else</param>
public readonly struct SQLConnectionLease(DbConnection connection, bool isOwner) : IAsyncDisposable
{
    public DbConnection Connection { get; } = connection;

    public ValueTask DisposeAsync()
    {
        if (isOwner)
            return Connection.DisposeAsync();

        return default;
    }
}
