using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Schema;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Schema;

internal class SchemaBuildCommand(SchemaBuilder schemaBuilder) : AsyncCommand<SchemaBuildCommand.Settings>
{
    internal class Settings : SchemaSourceSettings
    {
        [CommandOption("-o|--out-path <PATH>")]
        [Description("Path to write the dacpac into, created if it does not exist (default: a temp directory that is cleaned up)")]
        public string? OutPath { get; init; }

        [CommandOption("--platform <PLATFORM>")]
        [Description("Platform to build the schema against: Master (boxed SQL Server) or Azure (default: Azure)")]
        public SchemaSystemPlatform SystemPlatform { get; init; } = SchemaSystemPlatform.Azure;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Building schema");

        AnsiConsole.MarkupLineInterpolated($"[olive]Source:[/] [steelblue1]{SchemaSource.ToSchemaSourceType(settings.Source)}[/]");

        if (settings.Type == SchemaSourceType.Project)
            AnsiConsole.MarkupLineInterpolated($"[olive]Project:[/] [steelblue1]{settings.Project}[/]");

        AnsiConsole.MarkupLineInterpolated($"[olive]System platform:[/] [steelblue1]{settings.SystemPlatform}[/]");

        int result = await AnsiConsole.Status()
           .Spinner(Spinner.Known.BouncingBar)
           .SpinnerStyle(Style.Parse("gold1"))
           .StartAsync("[steelblue1]Building schema...[/]", async ctx =>
           {
               DirectoryInfo buildDir;

               try
               {
                   if (string.IsNullOrWhiteSpace(settings.OutPath))
                   {
                       AnsiConsole.MarkupLine("[olive]Creating work directory...[/]");

                       buildDir = Directory.CreateTempSubdirectory("migdb-build");
                   }
                   else
                   {
                       AnsiConsole.MarkupLineInterpolated($"[olive]Writing output to:[/] [steelblue1]{settings.OutPath}[/]");

                       buildDir = Directory.CreateDirectory(settings.OutPath);
                   }
               }
               catch (Exception e)
               {
                   SpectreUtils.WriteError(e.Message);
                   return ExitCode.UsageError;
               }

               try
               {
                   AnsiConsole.MarkupLineInterpolated($"[olive]Building {SchemaSource.ToSchemaSourceType(settings.Source)}...[/]");

                   string dacpac = await schemaBuilder.BuildSourceAsync(settings.Source, buildDir.FullName, settings.SystemPlatform, ct);

                   SpectreUtils.WriteSectionRule();

                   AnsiConsole.MarkupLineInterpolated($"[green]Build succeeded:[/] [steelblue1]{dacpac}[/]");

                   return ExitCode.Success;
               }
               // a missing source root surfaces as DirectoryNotFoundException out of the project lookup
               catch (Exception e) when (e is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
               {
                   RenderBuildFailure(e.Message);

                   Log.Error(e, "Schema build failed");

                   return ExitCode.Failure;
               }
               finally
               {
                   // the dacpac is the whole point when an out path was given, only temp dirs get cleaned up
                   if (string.IsNullOrWhiteSpace(settings.OutPath))
                       CleanUp(buildDir);
               }
           });

        return result;
    }

    /// <summary>
    /// Renders the build output carried on the exception message. MSBuild emits one diagnostic
    /// per line so errors and warnings are picked out, otherwise the detail only reaches the log file
    /// </summary>
    /// <param name="message">The build failure message to render</param>
    private static void RenderBuildFailure(string message)
    {
        SpectreUtils.WriteSectionRule();

        SpectreUtils.Error.MarkupLine("[red]Build failed[/]");
        SpectreUtils.Error.WriteLine();

        foreach (string line in message.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0)
                continue;

            string colour;

            if (trimmed.Contains(": error", StringComparison.OrdinalIgnoreCase))
                colour = "red";
            else if (trimmed.Contains(": warning", StringComparison.OrdinalIgnoreCase))
                colour = "yellow";
            else
                colour = "grey";

            SpectreUtils.Error.MarkupLine($"[{colour}]{Markup.Escape(trimmed)}[/]");
        }

        SpectreUtils.Error.WriteLine();
    }

    private static void CleanUp(DirectoryInfo directory)
    {
        try
        {
            AnsiConsole.MarkupLine("[olive]Cleaning up...[/]");

            directory.Delete(recursive: true);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "Failed to delete working directory {Dir}, will need to manually clean up (find in env tmp)", directory.Name);
        }
    }
}
