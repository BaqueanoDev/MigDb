using MigDb.CLI.Commands.Database;
using MigDb.CLI.Utils;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Data.Common;

namespace MigDb.CLI.Commands.Schema;

internal class SchemaJournalInitCommand(SchemaRepository schemaRepo, DatabaseOptions dbOptions, SQLConnectionFactory sqlFactory) : AsyncCommand<SchemaJournalInitCommand.Settings>
{
    internal class Settings : DatabaseSettings
    {
        [CommandOption("-r|--dry-run")]
        [Description("Dry run without making changes")]
        public bool DryRun { get; init; } = false;
    }

    private sealed record ResourceCheck(string Name, string Kind, Func<Task<bool>> ExistsCallback, Func<Task> CreateCallback);

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]Dry run detected, changes will not be applied[/]");
            SpectreUtils.WriteHeaderRule("Dry Run");
        }

        AnsiConsole.MarkupLine("[aqua]Attempting to initialize database[/]");

        await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction: null, ct);
        await using DbTransaction transaction = await scope.Connection.BeginTransactionAsync(ct);

        ResourceCheck[] checks =
        [
            new(dbOptions.SchemaName, "schema",
                () => schemaRepo.SchemaExistAsync(transaction, ct),
                () => schemaRepo.CreateSchemaAsync(transaction, ct)),
            new(dbOptions.RunTableName, "table",
                () => schemaRepo.MigrationRunTableExistsAsync(transaction, ct),
                () => schemaRepo.CreateMigrationRunTableAsync(transaction, ct)),

            new(dbOptions.DirectoryTableName, "table",
                () => schemaRepo.MigrationDirectoryTableExistsAsync(transaction, ct),
                () => schemaRepo.CreateMigrationDirectoryTableAsync(transaction, ct)),

            new(dbOptions.FileTableName, "table",
                () => schemaRepo.MigrationFileTableExistsAsync(transaction, ct),
                () => schemaRepo.CreateMigrationFileTableAsync(transaction, ct)),

            new(dbOptions.SchemaProgrammableTableName, "table",
                () => schemaRepo.SchemaProgrammableTableExistsAsync(transaction, ct),
                () => schemaRepo.CreateSchemaProgrammableTableAsync(transaction, ct)),

            new(dbOptions.ProgrammableTableName, "table",
                () => schemaRepo.MigrationProgrammableTableExistsAsync(transaction, ct),
                () => schemaRepo.CreateMigrationProgrammableTableAsync(transaction, ct)),

            new(dbOptions.RevertTableName, "table",
                () => schemaRepo.MigrationRevertTableExistsAsync(transaction, ct),
                () => schemaRepo.CreateMigrationRevertTableAsync(transaction, ct)),
        ];

        string foundMessage = "Resource already exists";
        string notFoundMessage = "Resource missing from database";

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("gold1"))
            .StartAsync("[olive]Attempting to initialize database...[/]", async ctx =>
            {

                await Task.Delay(200);

                foreach (ResourceCheck c in checks)
                {
                    string statusMessage = $"Checking [olive]{c.Kind}[/] - [gold1]{c.Name}[/] in database...";

                    AnsiConsole.MarkupLine(statusMessage);

                    bool resourceExists = await c.ExistsCallback();

                    string statusColor = resourceExists ? "green" : "red";
                    string status = resourceExists ? foundMessage : notFoundMessage;
                    string message = $"[{statusColor}]{status}[/]";

                    AnsiConsole.MarkupLine(message);

                    if (!resourceExists)
                    {
                        statusMessage = $"Attempting to create [olive]{c.Kind}[/] - [gold1]{c.Name}[/] in database";

                        AnsiConsole.MarkupLine(statusMessage);

                        if (!settings.DryRun)
                            await c.CreateCallback();

                        AnsiConsole.MarkupLine("Resource created [green]successfully[/]");
                    }

                    SpectreUtils.WriteSectionRule();
                }

                AnsiConsole.WriteLine("Commiting changes...");

                if (!settings.DryRun)
                    await transaction.CommitAsync(ct);

                SpectreUtils.WriteSectionRule();
                AnsiConsole.WriteLine("run 'journal status' to confirm changes");
            });

        return ExitCode.Success;
    }
}
