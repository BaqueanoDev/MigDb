using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Migration;
using MigDb.Core.Migration.Generate;
using MigDb.Core.Migration.Revert;
using MigDb.Core.Schema;
using MigDb.Core.Utils;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationRevertCommand(
    MigrationGenerator generator,
    MigrationPathResolver pathResolver,
    MigrationJournalReverter journalReverter) : AsyncCommand<MigrationRevertCommand.Settings>
{
    private sealed record RevertScriptResult(int Result, IReadOnlyList<MigrationScript> Scripts, IReadOnlyList<string> Reverted);

    internal class Settings : SchemaSourceSettings, IDatabaseConnectionSetting
    {
        [CommandOption("-o|--out-path <PATH>")]
        [Description("Path to write the revert scripts into, created if it does not exist")]
        public string? OutPath { get; init; }

        [CommandOption("--target <REVISION>")]
        [Description("Revision to revert back to: a git commit-ish (default: HEAD~1, the commit before the last one)")]
        public string? TargetRevisionSpec { get; init; }

        [CommandOption("-c|--connection <CONNECTION>")]
        [Description("Connection string to the database the reverted migrations were applied to. Supply it to preview the revert against that database, add --force to run it")]
        public string? Connection { get; init; }

        [CommandOption("-f|--force")]
        [Description("Run the revert scripts against the database and wind the journal back. Without it both are only previewed")]
        public bool Force { get; init; } = false;

        public SchemaRevision TargetRevision => SchemaRevision.Parse(TargetRevisionSpec ?? SchemaRevision.PreviousCommit);

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (string.IsNullOrWhiteSpace(OutPath))
                return ValidationResult.Error("--out-path is required, pass the path to write the revert scripts into");

            if (TargetRevision == SchemaRevision.Parse(SchemaRevision.HeadCommitish))
                return ValidationResult.Error("Cannot revert HEAD to itself, pass an earlier revision");

            if (Force && string.IsNullOrWhiteSpace(Connection))
                return ValidationResult.Error("--force needs a database to revert, pass --connection with it");

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Reverting last commit");

        MigrationSource migrationSource = settings.Source is SchemaSource.Project project
            ? new MigrationSource.Project(project.Name)
            : new MigrationSource.Common();

        SchemaRevision sourceRevision = settings.TargetRevision;
        SchemaRevision targetRevision = SchemaRevision.Parse(SchemaRevision.HeadCommitish);

        bool journal = !string.IsNullOrWhiteSpace(settings.Connection);

        RevertScriptResult scriptPass = await AnsiConsole.Status()
           .Spinner(Spinner.Known.BouncingBar)
           .SpinnerStyle(Style.Parse("gold1"))
           .StartAsync<RevertScriptResult>("[steelblue1]Reverting last commit...[/]", async ctx =>
           {
               DirectoryInfo outputDirectory;

               try
               {
                   outputDirectory = new(settings.OutPath!);
               }
               catch (Exception e)
               {
                   SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
                   return new(ExitCode.UsageError, [], []);
               }

               AnsiConsole.MarkupLineInterpolated($"[olive]Building & comparing source ({SchemaRevision.Describe(sourceRevision)}) & target ({SchemaRevision.Describe(targetRevision)})...[/]");

               IReadOnlyList<MigrationScript> scripts;

               try
               {
                   scripts = await generator.GenerateAsync(settings.Source, sourceRevision, targetRevision, SchemaDeployScope.All, blockOnDataLoss: !settings.Force, ct);
               }
               catch (Exception e)
               {
                   SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
                   return new(ExitCode.Failure, [], []);
               }

               if (scripts.Count == 0)
               {
                   AnsiConsole.MarkupLineInterpolated($"[olive]Comparison returned no differences...[/]");

                   if (journal)
                       AnsiConsole.MarkupLine("[yellow]Nothing to undo with, journal left alone[/]");

                   return new(ExitCode.Success, [], []);
               }

               try
               {
                   outputDirectory.Create();
               }
               catch (Exception e)
               {
                   SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
                   return new(ExitCode.Failure, [], []);
               }

               if (settings.Force)
                   AnsiConsole.MarkupLine("[orange1]--force: data loss guards are suppressed, destructive changes will run unguarded[/]");

               AnsiConsole.MarkupLineInterpolated($"[olive]Writing scripts to: [/][steelblue1]'{outputDirectory.FullName}'[/]");

               foreach (MigrationScript script in scripts)
               {
                   if (script.Warnings.Count != 0)
                   {
                       SpectreUtils.WriteSectionRule();
                       AnsiConsole.MarkupLineInterpolated($"[orange1]Warnings detected for: [/][steelblue1]'{script.Object.FullName}'[/]");
                       AnsiConsole.WriteLine();
                   }

                   foreach (string warning in script.Warnings)
                       AnsiConsole.MarkupLineInterpolated($"[yellow]{warning}[/]");

                   string fileName = script.FileName;

                   await File.WriteAllTextAsync(Path.Combine(outputDirectory.FullName, fileName), script.Script, ct);

                   AnsiConsole.WriteLine();
                   AnsiConsole.MarkupLineInterpolated($"[green]Writing:[/] [steelblue1]'{fileName}'[/]");
               }

               SpectreUtils.WriteSectionRule();

               if (!journal)
                   return new(ExitCode.Success, scripts, []);

               IReadOnlyList<string> revertedMigrations;

               try
               {
                   revertedMigrations = await ListRevertedMigrationsAsync(migrationSource, sourceRevision, targetRevision, ct);
               }
               catch (Exception e)
               {
                   SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
                   return new(ExitCode.Failure, [], []);
               }

               if (revertedMigrations.Count == 0)
                   AnsiConsole.MarkupLine("[olive]No migration directories were added between the two revisions, the journal has nothing to wind back[/]");

               return new(ExitCode.Success, scripts, revertedMigrations);
           });

        if (scriptPass.Result != ExitCode.Success)
            return scriptPass.Result;

        if (!journal || scriptPass.Scripts.Count == 0)
            return ExitCode.Success;

        return await RevertDatabaseAsync(settings, migrationSource, scriptPass.Scripts, scriptPass.Reverted, ct);
    }

    private async Task<IReadOnlyList<string>> ListRevertedMigrationsAsync(MigrationSource source, SchemaRevision from, SchemaRevision to, CancellationToken ct)
    {
        string root = pathResolver.ResolveMigrationSourceRoot(source);

        IReadOnlyList<string> current = await ListMigrationsAsync(root, to, ct);
        IReadOnlyList<string> previous = await ListMigrationsAsync(root, from, ct);

        return [.. current.Except(previous, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static async Task<IReadOnlyList<string>> ListMigrationsAsync(string root, SchemaRevision revision, CancellationToken ct)
    {
        if (revision is SchemaRevision.History history)
            return await GitUtils.ListDirectoriesAtCommitAsync(root, history.Commitish, ct);

        DirectoryInfo directory = new(root);

        return directory.Exists ? [.. directory.GetDirectories().Select(d => d.Name)] : [];
    }

    private async Task<int> RevertDatabaseAsync(Settings settings, MigrationSource source, IReadOnlyList<MigrationScript> scripts, IReadOnlyList<string> reverted, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Database");

        foreach (string name in reverted)
            AnsiConsole.MarkupLineInterpolated($"[olive]Reverted migration: {name}[/]");

        MigrationRevertPlan plan;

        try
        {
            plan = await journalReverter.RevertAsync(scripts, reverted, source, settings.Force, SchemaRevision.Describe(settings.TargetRevision), ct);
        }
        catch (MigrationSchemaNotInitialisedException e)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
            return ExitCode.NotInitialised;
        }
        catch (Exception e)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]Failed to revert: {e.Message}[/]");
            Log.Error(e, "Failed to revert");
            return ExitCode.Failure;
        }

        RenderPlan(plan, scripts, settings.Force);

        return ExitCode.Success;
    }

    private static void RenderPlan(MigrationRevertPlan plan, IReadOnlyList<MigrationScript> scripts, bool applied)
    {
        SpectreUtils.WriteSectionRule();

        string scriptLabel;

        if (applied)
            scriptLabel = "Executed script";
        else
            scriptLabel = "Execute script";

        foreach (MigrationScript script in scripts)
            AnsiConsole.MarkupLineInterpolated($"[aqua]{scriptLabel}[/]\t{script.Object.FullName}[grey]\t({script.Object.Kind})[/]");

        foreach (MigrationDirectoryEntity directory in plan.Directories)
            AnsiConsole.MarkupLineInterpolated($"[red]Drop directory[/]\t{directory.Name}[grey]\t({directory.Source:g})[/]");

        foreach (MigrationRunEntity run in plan.Runs)
            AnsiConsole.MarkupLineInterpolated($"[red]Drop run[/]\t{run.MigrationRunId}[grey]\t({run.AppliedBy})[/]");

        foreach (ProgrammableRewind rewind in plan.Programmables)
        {
            if (rewind.IsDrop)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Drop programmable[/]\t{rewind.Programmable.FullName}[grey]\t(introduced by a dropped run)[/]");
                continue;
            }

            string previous = Convert.ToHexString(rewind.PreviousHash!)[..8].ToLowerInvariant();

            AnsiConsole.MarkupLineInterpolated($"[aqua]Rewind programmable[/]\t{rewind.Programmable.FullName}[grey]\t(hash {previous})[/]");
        }

        AnsiConsole.WriteLine();

        if (applied)
        {
            AnsiConsole.MarkupLineInterpolated($"[green]Reverted {scripts.Count} object(s) in the database[/]");

            if (plan.Directories.Count != 0)
                AnsiConsole.MarkupLineInterpolated($"[green]Journal wound back, {plan.Directories.Count} migration(s) are pending again[/]");
        }
        else
            AnsiConsole.MarkupLine("[yellow]Preview only, nothing was written. Pass --force to run the scripts against the database and wind the journal back[/]");

        if (plan.Directories.Count != 0)
            AnsiConsole.MarkupLine("[yellow]Those directories still exist in the working tree, roll it back to the target revision before 'migration apply --pending' or they will simply be re-applied[/]");
    }
}
