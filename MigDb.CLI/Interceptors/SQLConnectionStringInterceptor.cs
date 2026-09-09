using MigDb.CLI.Commands.Settings;
using MigDb.Core.Infrastructure.Database;
using Spectre.Console.Cli;

namespace MigDb.CLI.Interceptors;

internal sealed class SQLConnectionStringInterceptor(SQLConnectionFactory factory) : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (settings is IDatabaseConnectionSetting db && !string.IsNullOrWhiteSpace(db.Connection))
            factory.ConnectionString = db.Connection;
    }
}