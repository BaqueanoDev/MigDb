using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Configuration;
using MigDb.CLI.Utils;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;

namespace MigDb.CLI.Commands;

internal class ShowLogCommand() : AsyncCommand<ShowLogCommand.Settings>
{
    internal class Settings : GlobalSettings
    {
        public override ValidationResult Validate()
        {
            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        string logPath = ConfigManager.LogFilePath;

        AnsiConsole.WriteLine("Opening log directory...");

        Process.Start(new ProcessStartInfo
        {
            FileName = logPath,
            UseShellExecute = true
        });

        return ExitCode.Success;
    }
}
