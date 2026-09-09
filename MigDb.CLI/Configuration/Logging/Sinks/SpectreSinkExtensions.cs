using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace MigDb.CLI.Configuration.Logging.Sinks;

internal static class SpectreSinkExtensions
{
    public static LoggerConfiguration Spectre(
        this LoggerSinkConfiguration sinkConfiguration,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
        LoggingLevelSwitch? levelSwitch = null,
        IFormatProvider? formatProvider = null)
    {
        return sinkConfiguration.Sink(new SpectreSink(levelSwitch, formatProvider), restrictedToMinimumLevel, levelSwitch);
    }
}
