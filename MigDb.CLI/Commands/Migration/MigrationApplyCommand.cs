using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Migration;
using MigDb.Core.Migration.Run;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Globalization;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationApplyCommand(MigrationSelector selector, MigrationRunner runner, SchemaProgrammableScanner scanner) : AsyncCommand<MigrationApplyCommand.Settings>
{
    internal enum SelectionMode
    {
        NameOrPath,
        Pending,
        Range,
    }

    internal class Settings : MigrationSourceSettings, IDatabaseConnectionSetting
    {
        [CommandArgument(0, "[name-or-path]")]
        [Description("Directory name (from migrations config) or an absolute path")]
        public string? NameOrPath { get; set; }

        [CommandOption("-p|--pending")]
        [Description("Apply all pending")]
        public bool Pending { get; init; } = false;

        [CommandOption("--tail")]
        [Description("Apply the tail of pending migrations (from latest applied onwards)")]
        public bool Tail { get; init; } = false;

        [CommandOption("--from <DateTimeStamp>")]
        [Description("Apply migrations on or after this date/time, e.g. 29/06/2026 or 29/06/2026 13:00 (range mode)")]
        public string? From { get; init; }

        [CommandOption("--to <DateTimeStamp>")]
        [Description("Apply migrations up to this date/time (defaults to now; requires --from)")]
        public string? To { get; init; }

        [CommandOption("-c|--connection <CONNECTION>")]
        [Description("Connection string to use")]
        public string? Connection { get; init; }

        [CommandOption("-r|--dry-run")]
        [Description("Dry run - applies inside a transaction then rolls back")]
        public bool DryRun { get; init; } = false;

        [CommandOption("--record-only")]
        [Description("Journal the selected migrations as applied without executing anything - for a database that already holds the changes. Programmables are journalled, not deployed")]
        public bool RecordOnly { get; init; } = false;

        [CommandOption("-f|--force")]
        [Description("Skip the --record-only confirmation prompt")]
        public bool Force { get; init; } = false;

        public SelectionMode Mode { get; private set; } = SelectionMode.NameOrPath;
        public DateTime? FromDate { get; private set; }
        public DateTime? ToDate { get; private set; }

        // List of possible valid formats
        private static readonly string[] TimestampFormats =
        [
            "dd/MM/yyyy",
            "dd/MM/yyyy HH",
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy HH:mm:ss",
            "yyyy/MM/dd",
            "yyyy/MM/dd HH",
            "yyyy/MM/dd HH:mm",
            "yyyy/MM/dd HH:mm:ss",
        ];

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (string.IsNullOrWhiteSpace(Connection))
                return ValidationResult.Error("--connection is required");

            bool hasNameOrPath = NameOrPath is not null;
            bool hasRange = From is not null;
            bool hasPending = Pending || Tail;

            if (!hasNameOrPath && !hasPending && !hasRange)
                return ValidationResult.Error("Specify exactly one selection mode: a name/path, --pending, or --from");

            if ((hasNameOrPath && hasPending) || (hasNameOrPath && hasRange) || (hasPending && hasRange))
                return ValidationResult.Error("Selection modes are mutually exclusive: use only one of a name/path, --pending, or --from");

            if (To is not null && From is null)
                return ValidationResult.Error("--to requires --from");

            if (From is not null)
            {
                if (!DateTime.TryParseExact(From, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime from))
                    return ValidationResult.Error($"--from is not a recognised date/time: '{From}'");

                FromDate = from;
            }

            if (To is not null)
            {
                if (!DateTime.TryParseExact(To, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime to))
                    return ValidationResult.Error($"--to is not a recognised date/time: '{To}'");

                ToDate = to;
            }

            if (hasPending)
                Mode = SelectionMode.Pending;
            else if (hasRange)
                Mode = SelectionMode.Range;

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        try
        {
            SpectreUtils.WriteHeaderRule(settings.RecordOnly ? "Recording Migration" : "Applying Migration");

            if (settings.DryRun)
                AnsiConsole.MarkupLine("[yellow]Dry run detected, changes will be rolled back[/]");

            if (settings.RecordOnly)
                AnsiConsole.MarkupLine("[yellow]Record only - migrations will be journalled as applied without executing anything[/]");

            AnsiConsole.WriteLine("Detecting directories");

            MigrationSource source = settings.Type.ToMigrationSource(settings.Project);

            List<MigrationDirectory> migrations;

            if (settings.Mode == SelectionMode.Pending)
            {
                if (settings.Tail)
                    migrations = await selector.GetPendingTailAsync(source, ct: ct);
                else
                    migrations = await selector.GetPendingAsync(source, ct: ct);
            }
            else if (settings.Mode == SelectionMode.Range)
            {
                migrations = selector.GetByDateRange(source, settings.FromDate!.Value, settings.ToDate);
            }
            else
            {
                if (settings.NameOrPath is null)
                {
                    SpectreUtils.Error.MarkupLineInterpolated($"[red]Somehow got invalid name or path?[/]");
                    return ExitCode.UsageError;
                }

                try
                {
                    var directory = selector.Resolve(source, settings.NameOrPath);
                    migrations = [directory];
                }
                catch (Exception e)
                {
                    SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
                    return ExitCode.UsageError;
                }
            }

            if (migrations.Count == 0)
                AnsiConsole.MarkupLine("[yellow]No migrations selected, checking programmables[/]");
            else
                AnsiConsole.MarkupLineInterpolated($"[olive]{migrations.Count} migration(s) selected[/]");

            foreach (var f in migrations)
                AnsiConsole.MarkupLineInterpolated($"[olive]{f.Name}[/]");

            SpectreUtils.WriteSectionRule();

            if (settings.RecordOnly && !settings.Force && !settings.DryRun)
            {
                AnsiConsole.MarkupLine("[yellow]These will be journalled as applied without running. Only do this if the database already holds them - the recorded hash becomes the drift baseline.[/]");

                bool confirm = await AnsiConsole.ConfirmAsync($"Record {migrations.Count} migration(s) as applied without executing them?", false, ct);

                if (!confirm)
                {
                    AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
                    return ExitCode.Success;
                }
            }

            SchemaSource schemaSource = source is MigrationSource.Project project
                ? new SchemaSource.Project(project.Name)
                : new SchemaSource.Common();

            IReadOnlyList<SchemaProgrammableFile> programmables = await scanner.LoadCommonWithProjectAsync(schemaSource, ct);

            AnsiConsole.MarkupLineInterpolated($"[olive]Checking ({programmables.Count}) programmable(s) for changes...[/]");

            MigrationRunMode mode = settings.RecordOnly ? MigrationRunMode.RecordOnly : MigrationRunMode.Apply;

            MigrationRunnerResult migrationResult = await runner.RunMigrationAsync(migrations, programmables, settings.DryRun, mode, ct);

            if (migrationResult.Result == MigrationRunResult.ValidationFailed)
            {
                RenderValidationFailures(migrationResult.Validations);
                return ExitCode.ValidationFailed;
            }

            RenderResult(migrationResult);
            RenderExclusions(migrationResult.ProgrammablesExcluded);

            int migrationTimeSeconds = migrationResult.Duration;

            AnsiConsole.MarkupLineInterpolated($"[olive]Migration completed in {migrationTimeSeconds} ms, Changes: {migrationResult.Changes}[/]");

            return ExitCode.Success;
        }
        catch (MigrationSchemaNotInitialisedException e)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
            return ExitCode.NotInitialised;
        }
        catch (Exception e)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]Failed to apply migration: {e.Message}[/]");
            Log.Error(e, "Failed to apply migration");
            return ExitCode.Failure;
        }
    }

    private static void RenderValidationFailures(IReadOnlyList<MigrationValidationResult> validations)
    {
        SpectreUtils.WriteHeaderRule("Validation Failed");

        SpectreUtils.Error.MarkupLineInterpolated($"[red]{validations.Count} item(s) failed validation, nothing was applied[/]");
        AnsiConsole.WriteLine();

        SpectreUtils.WriteValidationFailures(validations);
    }

    private static void RenderExclusions(IReadOnlyList<SchemaProgrammableFile> excluded)
    {
        if (excluded.Count == 0)
            return;

        SpectreUtils.WriteHeaderRule("Programmables Skipped");

        AnsiConsole.MarkupLineInterpolated($"[yellow]{excluded.Count} programmable(s) cannot be redeployed with CREATE OR ALTER and were left alone. Change these through a migration.[/]");
        AnsiConsole.WriteLine();

        foreach (SchemaProgrammableFile p in excluded)
            AnsiConsole.MarkupLineInterpolated($"[yellow]{SpectreUtils.DescribeExclusion(p.Exclusion)}[/]\t{p.FullName}[grey]\t({p.Type} · {p.FileInfo.Name})[/]");

        AnsiConsole.WriteLine();
    }

    private static void RenderResult(MigrationRunnerResult result)
    {
        if (result.TablesChanged.Count == 0 && result.ProgrammablesChanged.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No objects changed[/]");
            return;
        }

        SpectreUtils.WriteHeaderRule("Objects Changed");

        foreach (SchemaObject o in result.TablesChanged)
            AnsiConsole.MarkupLineInterpolated($"[green]Migration[/]\t{o.FullName}[grey]\t({o.Kind})[/]");

        foreach (SchemaProgrammableFile p in result.ProgrammablesChanged)
            AnsiConsole.MarkupLineInterpolated($"[aqua]Programmable[/]\t{p.FullName}[grey]\t({p.Type} · {p.FileInfo.Name})[/]");

        AnsiConsole.WriteLine();
    }
}
