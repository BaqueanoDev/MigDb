using MigDb.Core.Options;

namespace MigDb.CLI.Configuration;

internal sealed class ConfigOptions
{
    public DatabaseOptions Database { get; set; } = new();
    public MigrationOptions Migration { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}
