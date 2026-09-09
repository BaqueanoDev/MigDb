using MigDb.CLI.Commands.Database;
using MigDb.CLI.Utils;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Data.Common;

namespace MigDb.CLI.Commands.Schema;

internal class SchemaJournalResetCommand(SchemaRepository schemaRepo, SQLConnectionFactory sqlFactory) : AsyncCommand<SchemaJournalResetCommand.Settings>
{
    internal class Settings : DatabaseSettings
    {
        [CommandOption("-f|--force")]
        [Description("Bypass prompts and force reset")]
        public bool Force { get; init; } = false;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Database Reset");

        AnsiConsole.WriteLine("Attempting to reset database");
        AnsiConsole.MarkupLineInterpolated($"Database: {settings.Connection}");

        if (!settings.Force)
        {
            AnsiConsole.WriteLine();
            bool confirmA = await AnsiConsole.ConfirmAsync("This will wipe all migration data, are you sure?", false, ct);

            if (!confirmA)
            {
                AnsiConsole.WriteLine("Reset Cancelled");
                return ExitCode.Success;
            }

            AnsiConsole.WriteLine();
            bool confirmB = await AnsiConsole.ConfirmAsync("I dont believe you, are you sure?", false, ct);

            if (!confirmB)
            {
                AnsiConsole.WriteLine("Reset Cancelled");
                return ExitCode.Success;
            }

            AnsiConsole.WriteLine();
        }

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("gold1"))
            .StartAsync("[olive]Attempting to reset database...[/]", async ctx =>
            {
                await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(transaction: null, ct);
                await using DbTransaction transaction = await scope.Connection.BeginTransactionAsync(ct);

                await schemaRepo.DropSchemaTablesAsync(transaction, ct);
                await schemaRepo.DropSchemaAsync(transaction, ct);

                SpectreUtils.WriteSectionRule();

                AnsiConsole.MarkupLine("[gold1]Creating schema[/]");
                await schemaRepo.CreateSchemaAsync(transaction, ct);

                AnsiConsole.MarkupLine("[gold1]Creating tables[/]");
                await schemaRepo.CreateSchemaTablesAsync(transaction, ct);

                await transaction.CommitAsync();
            });

        return ExitCode.Success;
    }
}
