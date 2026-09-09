using MigDb.CLI.Commands.Database;
using MigDb.CLI.Utils;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Options;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MigDb.CLI.Commands.Schema;

internal class SchemaJournalStatusCommand(SchemaRepository schemaRepo, DatabaseOptions dbOptions) : AsyncCommand<SchemaJournalStatusCommand.Settings>
{
    private sealed record ResourceCheck(
    string Name,
    string Kind,
    string FoundMessage,
    string MissingMessage,
    Func<Task<bool>> CheckFunc);

    internal class Settings : DatabaseSettings
    {
        public override ValidationResult Validate()
        {
            ValidationResult b = base.Validate();

            if (!b.Successful)
                return b;

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        const int StatusColIdx = 2;
        const int MessageColIdx = 3;

        Log.Debug("Checking database status...");

        ResourceCheck[] checks =
        [
            new(dbOptions.SchemaName, "schema", "Schema Already Exists", "Schema doesn't exist", () => schemaRepo.SchemaExistAsync(ct: ct)),
            new(dbOptions.SchemaProgrammableTableName, "table", "Table Found", "Table Missing", () => schemaRepo.SchemaProgrammableTableExistsAsync(ct: ct)),
            new(dbOptions.RunTableName, "table", "Table Found", "Table Missing", () => schemaRepo.MigrationRunTableExistsAsync(ct: ct)),
            new(dbOptions.DirectoryTableName,"table", "Table Found", "Table Missing", () => schemaRepo.MigrationDirectoryTableExistsAsync(ct: ct)),
            new(dbOptions.FileTableName, "table", "Table Found", "Table Missing", () => schemaRepo.MigrationFileTableExistsAsync(ct: ct)),
            new(dbOptions.ProgrammableTableName, "table", "Table Found", "Table Missing", () => schemaRepo.MigrationProgrammableTableExistsAsync(ct: ct)),
            new(dbOptions.RevertTableName, "table", "Table Found", "Table Missing", () => schemaRepo.MigrationRevertTableExistsAsync(ct: ct)),
        ];

        Table table = new Table()
            .AsciiDoubleHeadBorder()
            .BorderColor(Color.SteelBlue)
            .ShowRowSeparators()
            .AddColumn("[DodgerBlue1]Resource[/]")
            .AddColumn("[DodgerBlue1]Type[/]")
            .AddColumn("[DodgerBlue1]Status[/]")
            .AddColumn("[DodgerBlue1]Message[/]");

        foreach (ResourceCheck c in checks)
            table.AddRow(c.Name, c.Kind, "[orange1]Pending[/]", "-");

        Panel panel = new Panel(table)
            .Header("[DodgerBlue1]Database Resources[/]", Justify.Center)
            .NoBorder();

        bool missingResource = false;

        await AnsiConsole.Live(Align.Center(panel)).StartAsync(async ctx =>
        {
            for (int i = 0; i < checks.Length; i++)
            {
                ResourceCheck c = checks[i];
                Log.Debug("Checking {Kind} {Name} in database...", c.Kind, c.Name);

                bool resourceResult = await c.CheckFunc();

                string statusColor = resourceResult ? "green" : "red";
                string status = resourceResult ? "Found" : "Missing";
                string message = resourceResult ? c.FoundMessage : c.MissingMessage;

                Log.Debug("Resource {Kind} {Name} - {Status}", c.Kind, c.Name, status);

                table.UpdateCell(i, StatusColIdx, $"[{statusColor}]{status}[/]");
                table.UpdateCell(i, MessageColIdx, $"[{statusColor}]{message}[/]");

                missingResource |= !resourceResult;
                ctx.Refresh();
            }
        });

        AnsiConsole.WriteLine();

        if (missingResource)
            AnsiConsole.MarkupLine("[yellow]One or more resources are missing from database![/]");

        return ExitCode.Success;
    }
}
