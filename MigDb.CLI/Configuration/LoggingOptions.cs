using Serilog.Events;

namespace MigDb.CLI.Configuration;

internal sealed class LoggingOptions
{
    public LogEventLevel ConsoleLevel { get; set; } = LogEventLevel.Warning;
    public LogEventLevel FileLevel { get; set; } = LogEventLevel.Verbose;
}
