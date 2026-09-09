using Microsoft.Data.SqlClient;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics.CodeAnalysis;

namespace MigDb.CLI.Configuration.Logging;

internal sealed class ConnectionStringDestructuringPolicy : IDestructuringPolicy
{
    private const string SecretUser = "******";
    private const string SecretPassword = "******";

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        if (value is not SqlConnectionStringBuilder builder)
        {
            result = null;
            return false;
        }

        result = new StructureValue(
        [
            new LogEventProperty("server", new ScalarValue(builder.DataSource)),
            new LogEventProperty("database", new ScalarValue(builder.InitialCatalog)),
            new LogEventProperty("user", new ScalarValue(SecretUser)),
            new LogEventProperty("password", new ScalarValue(SecretPassword)),
        ]);

        return true;
    }
}
