using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Configuration;
using MigDb.CLI.Utils;
using Serilog;
using Serilog.Events;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MigDb.CLI.Interceptors;

internal sealed class CommandInterceptor : ICommandInterceptor
{

    public void Intercept(CommandContext context, CommandSettings settings)
    {
        ApplyVerbosity(settings);

        // dont log command params in case of sensitive data
        string command = string.Join(' ', context.Arguments.TakeWhile(a => a == "--help" || !a.StartsWith('-')));

        Log.Information("Executing command: {Command}", command);
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result)
    {
        SpectreUtils.WriteSectionRule();

        if (result == ExitCode.Success)
        {
            AnsiConsole.MarkupLine("[green]done![/]");
            Log.Information("Command completed {ExitCode}", result);
        }
        else
        {
            SpectreUtils.WriteError("error!");
            Log.Error("Command {ExitCode}", result);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    private static void ApplyVerbosity(CommandSettings settings)
    {
        if (settings is not GlobalSettings global)
            return;

        LogEventLevel level;

        if (global.Debug)
            level = LogEventLevel.Verbose;
        else if (global.Verbose)
            level = LogEventLevel.Information;
        else if (global.Quiet)
            level = LogEventLevel.Error;
        else
            return;

        ConfigManager.ConsoleLogLevelSwitch.MinimumLevel = level;
    }
}
