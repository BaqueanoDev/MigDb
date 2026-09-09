using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Migration;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationShowCommand(MigrationSelector selector) : AsyncCommand<MigrationShowCommand.Settings>
{
    internal class Settings : MigrationSourceSettings
    {

        [CommandArgument(0, "<NAME>")]
        [Description("Name of migration directory")]
        public required string Name { get; set; }

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
        AnsiConsole.WriteLine("Opening migration directory...");

        MigrationSource source = settings.Type.ToMigrationSource(settings.Project);

        MigrationDirectory migration = selector.Get(source, settings.Name);

        if (!migration.Info.Exists)
        {
            SpectreUtils.WriteError("Migration directory doesnt exists");
            return ExitCode.UsageError;
        }

        string path = migration.FullName;

        AnsiConsole.WriteLine("Directory found, attempting to open");

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });

        return ExitCode.Success;
    }
}
