using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Migration;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationCreateCommand(MigrationDirectoryFactory directoryFactory) : AsyncCommand<MigrationCreateCommand.Settings>
{
    internal class Settings : MigrationSourceSettings
    {
        [CommandArgument(0, "[name]")]
        [Description("Name of directory")]
        public string? Name { get; set; }

        [CommandOption("-o|--out-path <PATH>")]
        [Description("Path to create the directory in (bypasses the configured migrations root)")]
        public string? OutPath { get; set; }

        [CommandOption("-r|--reveal")]
        [Description("Reveal directory after creating (windows only)")]
        public bool Reveal { get; set; } = false;

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        string path;

        SpectreUtils.WriteHeaderRule("Creating migration directory");

        if (settings.OutPath is null)
            path = directoryFactory.CreateMigrationDirectory(settings.Type.ToMigrationSource(settings.Project), settings.Name).FullName;
        else
            path = MigrationDirectoryFactory.CreateMigrationDirectoryAt(settings.OutPath, settings.Name).FullName;

        AnsiConsole.MarkupLineInterpolated($"Created migration directory: {path}");

        if (settings.Reveal)
        {
            AnsiConsole.WriteLine("Revealing directory....");

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/n,\"{path}\"",
                UseShellExecute = true,
            });
        }

        return ExitCode.Success;
    }
}
