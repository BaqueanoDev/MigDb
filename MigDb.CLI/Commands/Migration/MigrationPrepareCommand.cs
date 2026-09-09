using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Migration;
using MigDb.Core.Migration.Manifest;
using MigDb.Core.Migration.Plan;
using MigDb.Core.Migration.Validation;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationPrepareCommand(MigrationSelector selector, MigrationPlanner planner, MigrationManifestStore manifestStore) : AsyncCommand<MigrationPrepareCommand.Settings>
{
    internal class Settings : MigrationSourceSettings
    {
        [CommandArgument(0, "<name-or-path>")]
        [Description("Directory name (from migrations config) or an absolute path")]
        public required string NameOrPath { get; set; }

        [CommandOption("-r|--dry-run")]
        [Description("Dry run without making changes")]
        public bool DryRun { get; init; } = false;

        [CommandOption("--no-split")]
        [Description("Fail on a batch separator instead of splitting the file into one file per batch")]
        public bool NoSplit { get; init; } = false;

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (string.IsNullOrWhiteSpace(NameOrPath))
                return ValidationResult.Error("Name or path cannot be empty");

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]Dry run detected, changes will not be applied[/]");
            SpectreUtils.WriteHeaderRule("Dry Run");
        }

        int result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("gold1"))
            .StartAsync<int>("[steelblue1]Preparing migration plan...[/]", async ctx =>
            {
                MigrationSource source = settings.Type.ToMigrationSource(settings.Project);

                MigrationDirectory directory;

                try
                {
                    directory = selector.Resolve(source, settings.NameOrPath);
                }
                catch (Exception e)
                {
                    SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
                    return ExitCode.UsageError;
                }

                SpectreUtils.WriteHeaderRule("Preparing");
                AnsiConsole.MarkupLineInterpolated($"Preparing directory: [olive]{directory.Name}[/]");

                AnsiConsole.WriteLine("Creating plan");

                MigrationPlan plan;

                try
                {
                    plan = await planner.CreateMigrationPlanAsync(directory, !settings.NoSplit, ct);
                }
                catch (MigrationValidationException e)
                {
                    SpectreUtils.Error.MarkupLine("[red]Directory failed validation[/]");

                    foreach (MigrationValidationError err in e.Errors)
                        SpectreUtils.Error.MarkupLineInterpolated($"[red]Error: {err.Describe()}[/]");

                    return ExitCode.ValidationFailed;
                }

                foreach (MigrationPlanFile f in plan.Files)
                    AnsiConsole.MarkupLineInterpolated($"Preparing file: [olive]{f.File.Name}[/] [steelblue1]-->[/] [olive]{f.GeneratedName}[/]");

                AnsiConsole.WriteLine("Applying plan");
                planner.ApplyMigrationPlan(plan, settings.DryRun);

                if (!settings.DryRun)
                {
                    AnsiConsole.WriteLine("Creating Manifest");

                    MigrationManifest manifest = await manifestStore.CreateManifestAsync(plan.Directory, ct);
                    manifestStore.WriteManifest(plan.Directory, manifest);
                }

                return ExitCode.Success;
            });

        return result;
    }
}
