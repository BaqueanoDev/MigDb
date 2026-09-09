using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Schema;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Schema;

internal class SchemaDriftCommand(SchemaBuilder schemaBuilder,
    SchemaComparer schemaComparer,
    SQLConnectionFactory sqlFactory) : AsyncCommand<SchemaDriftCommand.Settings>
{
    internal class Settings : SchemaSourceSettings, IDatabaseConnectionSetting
    {
        [CommandOption("-c|--connection <CONNECTION>")]
        [Description("Connection string to use")]
        public string? Connection { get; init; }

        [CommandOption("--platform <PLATFORM>")]
        [Description("Platform to build the schema against: Master (boxed SQL Server) or Azure (default: Azure)")]
        public SchemaSystemPlatform SystemPlatform { get; init; } = SchemaSystemPlatform.Azure;

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (string.IsNullOrWhiteSpace(Connection))
                return ValidationResult.Error("--connection is required");

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Checking for drift");

        AnsiConsole.MarkupLineInterpolated($"[olive]Source:[/] [steelblue1]{SchemaSource.ToSchemaSourceType(settings.Source)}[/]");

        if (settings.Type == SchemaSourceType.Project)
            AnsiConsole.MarkupLineInterpolated($"[olive]Project:[/] [steelblue1]{settings.Project}[/]");

        SchemaDriftReport? report = null;
        Exception? failure = null;

        int result = await AnsiConsole.Status()
           .Spinner(Spinner.Known.BouncingBar)
           .SpinnerStyle(Style.Parse("gold1"))
           .StartAsync("[steelblue1]Checking for drift...[/]", async ctx =>
           {
               AnsiConsole.MarkupLine("[olive]Creating work directory...[/]");

               DirectoryInfo buildDir = Directory.CreateTempSubdirectory("migdb-drift");

               try
               {
                   AnsiConsole.MarkupLineInterpolated($"[olive]Building {SchemaSource.ToSchemaSourceType(settings.Source)}...[/]");

                   string dacpac = await schemaBuilder.BuildSourceAsync(settings.Source, buildDir.FullName, settings.SystemPlatform, ct);

                   AnsiConsole.MarkupLine("[olive]Comparing against the database...[/]");

                   report = await schemaComparer.CompareToDatabaseAsync(dacpac, sqlFactory.ConnectionString ?? string.Empty, ct);

                   return report.HasDrift ? ExitCode.DriftDetected : ExitCode.Success;
               }
               catch (Exception e)
               {
                   failure = e;

                   Log.Error(e, "Drift check failed");

                   return ExitCode.Failure;
               }
               finally
               {
                   CleanUp(buildDir);
               }
           });

        if (failure is not null)
            SpectreUtils.WriteError(failure.Message);

        if (report is not null)
        {
            SpectreUtils.WriteSectionRule();

            WriteDriftReport(report);
        }

        return result;
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

    public static void WriteDriftReport(SchemaDriftReport report)
    {
        if (!report.HasDrift)
        {
            AnsiConsole.MarkupLineInterpolated($"[green]No drift:[/] [steelblue1]{report.Database}[/] [green]matches the schema project[/]");
            return;
        }

        AnsiConsole.MarkupLineInterpolated($"[yellow]{report.Items.Count} drifted object(s) in {report.Database}:[/]");

        Tree tree = new("[yellow]Drift[/]");

        AddDriftNode(tree, report, SchemaDriftKind.MissingFromProject, "In the database, not in the schema project");
        AddDriftNode(tree, report, SchemaDriftKind.MissingFromDatabase, "In the schema project, not in the database");
        AddDriftNode(tree, report, SchemaDriftKind.Different, "Defined differently in each");

        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();
    }

    private static void AddDriftNode(Tree tree, SchemaDriftReport report, SchemaDriftKind kind, string heading)
    {
        List<SchemaDriftItem> items = [.. report.OfKind(kind)];

        if (items.Count == 0)
            return;

        TreeNode node = tree.AddNode($"[yellow]{heading} ({items.Count})[/]");

        foreach (SchemaDriftItem item in items.OrderBy(i => i.ObjectType).ThenBy(i => i.Name))
        {
            TreeNode objectNode = node.AddNode($"[grey]{Markup.Escape(item.ObjectType)}[/] {Markup.Escape(item.Name)}");

            foreach (string detail in item.Details)
                objectNode.AddNode($"[grey]{Markup.Escape(detail)}[/]");
        }
    }
}
