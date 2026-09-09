using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration;
using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Data.Common;
using System.Transactions;

namespace MigDb.CLI.Commands.Schema;

internal class SchemaBaselineCommand(SQLConnectionFactory sqlFactory,
    SchemaRepository schemaRepo,
    SchemaBuilder schemaBuilder,
    SchemaProgrammableScanner scanner,
    SchemaProgrammableRunner runner) : AsyncCommand<SchemaBaselineCommand.Settings>
{
    internal class Settings : SchemaSourceSettings, IDatabaseConnectionSetting
    {
        [CommandOption("-c|--connection <CONNECTION>")]
        [Description("Connection string to use")]
        public string? Connection { get; init; }

        [CommandOption("--revision <REVISION>")]
        [Description("Revision to baseline from: a git commit-ish or 'working' (default: working)")]
        public string? Commitish { get; init; }

        [CommandOption("-r|--dry-run")]
        [Description("Dry run - records inside a transaction then rolls back")]
        public bool DryRun { get; init; } = false;

        public SchemaRevision Revision => SchemaRevision.Parse(Commitish ?? SchemaRevision.WorkingTreeToken);

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
        SpectreUtils.WriteHeaderRule("Baselining programmables");

        SchemaSource source = settings.Source;

        AnsiConsole.MarkupLineInterpolated($"[olive]Source:[/] [steelblue1]{SchemaSource.ToSchemaSourceType(source)}[/]");
        AnsiConsole.MarkupLineInterpolated($"[olive]Revision:[/] [steelblue1]{SchemaRevision.Describe(settings.Revision)}[/]");

        if (settings.Type == SchemaSourceType.Project)
            AnsiConsole.MarkupLineInterpolated($"[olive]Project:[/] [steelblue1]{settings.Project}[/]");

        if (settings.DryRun)
            AnsiConsole.MarkupLine("[yellow]Dry run - the journal is rolled back at the end[/]");

        try
        {
            SchemaProgrammableRunResult result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.BouncingBar)
                .SpinnerStyle(Style.Parse("gold1"))
                .StartAsync("[steelblue1]Baselining programmables...[/]", async ctx =>
                {
                    await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(null, ct);
                    await using DbTransaction transaction = await scope.Connection.BeginTransactionAsync(ct);

                    if (!await schemaRepo.MigrationSchemaInitialisedAsync(transaction, ct))
                        throw new MigrationSchemaNotInitialisedException();

                    DirectoryInfo workDir = Directory.CreateTempSubdirectory("migdb-baseline");

                    try
                    {
                        List<SchemaProgrammableFile> programmables = [];

                        SchemaSource common = new SchemaSource.Common();
                        string commonRoot = await schemaBuilder.ResolveSourceRootAsync(common, settings.Revision, workDir.FullName, "common", ct);

                        programmables.AddRange(await scanner.LoadFromRootAsync(commonRoot, common, ct));

                        if (source is SchemaSource.Project)
                        {
                            string projectRoot = await schemaBuilder.ResolveSourceRootAsync(source, settings.Revision, workDir.FullName, "project", ct);

                            programmables.AddRange(await scanner.LoadFromRootAsync(projectRoot, source, ct));
                        }

                        AnsiConsole.MarkupLineInterpolated($"[olive]Scanned {programmables.Count} programmable(s)[/]");

                        int databaseLockResult = await schemaRepo.AcquireMigrationLockAsync(transaction, ct);

                        if (databaseLockResult < 0)
                        {
                            Log.Warning("Could not acquire migration lock (result {Result}) - another migration may be in progress", databaseLockResult);
                            throw new MigrationAppLockException(databaseLockResult);
                        }

                        SchemaProgrammableRunResult runResult = await runner.RunAsync(programmables, SchemaProgrammableRunMode.JournalOnly, transaction, ct);

                        if (settings.DryRun)
                            await transaction.RollbackAsync(ct);
                        else
                            await transaction.CommitAsync(ct);

                        return runResult;
                    }
                    finally
                    {
                        try
                        {
                            workDir.Delete(recursive: true);
                        }
                        catch (IOException ex)
                        {
                            Log.Warning(ex, "Failed to delete working directory {Dir}, will need to manually clean up (find in env tmp)", workDir.Name);
                        }
                    }
                });

            SpectreUtils.WriteSectionRule();

            if (result.Deploys.Count > 0)
            {
                Table table = new Table()
                    .AsciiDoubleHeadBorder()
                    .BorderColor(Color.SteelBlue)
                    .AddColumn(new TableColumn("[DodgerBlue1]Object[/]").NoWrap())
                    .AddColumn("[DodgerBlue1]Type[/]")
                    .AddColumn("[DodgerBlue1]Recorded[/]");

                foreach (SchemaProgrammableDeploy d in result.Deploys)
                {
                    string recorded = d.Journal.HashBefore is null ? "new" : "changed";

                    table.AddRow(
                        Markup.Escape(d.Programmable.FullName),
                        Markup.Escape(d.Programmable.Type.ToString("g")),
                        recorded);
                }

                AnsiConsole.Write(table);
            }

            if (result.Excluded.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLineInterpolated($"[yellow]{result.Excluded.Count} programmable(s) were not baselined - they cannot be redeployed with CREATE OR ALTER, so the sweep never owns them. Change these through a migration.[/]");
                AnsiConsole.WriteLine();

                foreach (SchemaProgrammableFile p in result.Excluded)
                    AnsiConsole.MarkupLineInterpolated($"[yellow]{SpectreUtils.DescribeExclusion(p.Exclusion)}[/]\t{p.FullName}[grey]\t({p.Type} · {p.FileInfo.Name})[/]");

                AnsiConsole.WriteLine();
            }

            AnsiConsole.MarkupLineInterpolated($"[olive]Baselined {result.DeployedCount} of {result.TotalCount} - new: {result.Created}, changed: {result.Updated}, already current: {result.Skipped}, excluded: {result.Excluded.Count}[/]");

            return ExitCode.Success;
        }
        catch (MigrationSchemaNotInitialisedException e)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
            return ExitCode.NotInitialised;
        }
        catch (Exception e)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]Failed to baseline programmables: {e.Message}[/]");
            Log.Error(e, "Failed to baseline programmables");
            return ExitCode.Failure;
        }
    }
}