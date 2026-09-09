using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Migration;
using MigDb.Core.Migration.Generate;
using MigDb.Core.Schema;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationGenerateCommand(MigrationGenerator generator, MigrationSelector selector, MigrationDirectoryFactory directoryFactory) : AsyncCommand<MigrationGenerateCommand.Settings>
{
    internal class Settings : SchemaSourceSettings
    {
        [CommandOption("-o|--out-path <NAME-OR-PATH>")]
        [Description("Migration folder name or an absolute path to write into. Omit to create a new timestamped migration directory under the source")]
        public string? NameOrPath { get; set; }

        [CommandOption("--source <REVISION>")]
        [Description("Revision to build the source from: a git commit-ish or 'working' (default: working)")]
        public string? SourceRevisionSpec { get; init; }

        [CommandOption("--target <REVISION>")]
        [Description("Revision to build the target from: a git commit-ish, or 'working' (default: HEAD)")]
        public string? TargetRevisionSpec { get; init; }

        [CommandOption("--scope <SCOPE>")]
        [Description("Scope to generate: All, Schema (tables only) or Programmables (default: Schema). Migration directories are meant to hold Schema only - programmables ship through the hash gated programmable pipeline")]
        public SchemaDeployScope GenerateScope { get; init; } = SchemaDeployScope.Schema;

        public SchemaRevision SourceRevision => SchemaRevision.Parse(SourceRevisionSpec ?? SchemaRevision.WorkingTreeToken);
        public SchemaRevision TargetRevision => SchemaRevision.Parse(TargetRevisionSpec ?? SchemaRevision.HeadCommitish);

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (SourceRevision == TargetRevision)
                return ValidationResult.Error($"Source and target cannot be the same");

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Generating migration");

        int result = await AnsiConsole.Status()
           .Spinner(Spinner.Known.BouncingBar)
           .SpinnerStyle(Style.Parse("gold1"))
           .StartAsync("[steelblue1]Generating migration...[/]", async ctx =>
           {
               MigrationSource migrationSource = settings.Source is SchemaSource.Project project
                   ? new MigrationSource.Project(project.Name)
                   : new MigrationSource.Common();

               MigrationDirectory? outputDirectory = null;

               if (!string.IsNullOrWhiteSpace(settings.NameOrPath))
               {
                   try
                   {
                       outputDirectory = selector.Resolve(migrationSource, settings.NameOrPath);
                   }
                   catch (Exception e)
                   {
                       SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
                       return ExitCode.UsageError;
                   }
               }

               AnsiConsole.MarkupLineInterpolated($"[olive]Building & comparing {settings.GenerateScope} source ({SchemaRevision.Describe(settings.SourceRevision)}) & target ({SchemaRevision.Describe(settings.TargetRevision)})...[/]");

               IReadOnlyList<MigrationScript> scripts;

               try
               {
                   scripts = await generator.GenerateAsync(settings.Source, settings.SourceRevision, settings.TargetRevision, settings.GenerateScope, ct: ct);
               }
               catch (Exception e)
               {
                   SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
                   return ExitCode.Failure;
               }

               if (scripts.Count == 0)
               {
                   AnsiConsole.MarkupLineInterpolated($"[olive]Comparison returned no differences...[/]");
                   return ExitCode.Success;
               }

               outputDirectory ??= directoryFactory.CreateMigrationDirectory(migrationSource, null);

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

               return ExitCode.Success;
           });

        return result;
    }
}
