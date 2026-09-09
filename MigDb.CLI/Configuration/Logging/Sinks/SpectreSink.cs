using MigDb.CLI.Utils;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Spectre.Console;
using System.Text;

namespace MigDb.CLI.Configuration.Logging.Sinks;

internal sealed class SpectreSink(LoggingLevelSwitch? levelSwitch = null, IFormatProvider? formatProvider = null) : ILogEventSink
{
    private readonly LoggingLevelSwitch? _levelSwitch = levelSwitch;
    private readonly IFormatProvider? _formatProvider = formatProvider;

    private bool Verbose => _levelSwitch is not null && _levelSwitch.MinimumLevel <= LogEventLevel.Debug;

    public void Emit(LogEvent logEvent)
    {
        string message = RenderWithEscapedProperties(logEvent, _formatProvider);
        string severityColor = GetSeverityColor(logEvent.Level);
        IAnsiConsole console = GetConsole(logEvent.Level);

        console.MarkupLine($"[{severityColor}]{logEvent.Level}:[/] {message}");

        if (logEvent.Exception is null)
            return;

        if (Verbose)
            console.WriteException(logEvent.Exception);
        else
            console.MarkupLineInterpolated($"[{severityColor}]  {logEvent.Exception.GetType().Name}: {logEvent.Exception.Message}[/]");
    }

    private static string RenderWithEscapedProperties(LogEvent logEvent, IFormatProvider? formatProvider)
    {
        StringBuilder sb = new();

        foreach (MessageTemplateToken token in logEvent.MessageTemplate.Tokens)
        {
            switch (token)
            {
                case TextToken text:
                    sb.Append(text.Text);
                    break;

                case PropertyToken prop:
                    using (StringWriter writer = new())
                    {
                        prop.Render(logEvent.Properties, writer, formatProvider);
                        sb.Append(Markup.Escape(writer.ToString()));
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private static IAnsiConsole GetConsole(LogEventLevel level)
    {
        if (level >= LogEventLevel.Error)
            return SpectreUtils.Error;

        return AnsiConsole.Console;
    }

    private static string GetSeverityColor(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Fatal => "red",
            LogEventLevel.Error => "red",
            LogEventLevel.Warning => "yellow",
            LogEventLevel.Information => "aqua",
            LogEventLevel.Debug => "grey",
            LogEventLevel.Verbose => "dim",
            _ => "grey",
        };
    }
}
