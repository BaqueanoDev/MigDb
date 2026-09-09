using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Repositories;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MigDb.CLI.Commands.Database;

internal class DatabaseMigrationListCommand(MigrationDirectoryRepository directoryRepository) : AsyncCommand<DatabaseMigrationListCommand.Settings>
{
    internal class Settings : DatabaseSettings
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        IReadOnlyList<MigrationDirectoryEntity> migrations = await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("gold1"))
            .StartAsync("[steelblue1]Listing applied migrations...[/]", async ctx =>
            {
                SpectreUtils.WriteHeaderRule("Listing applied migrations");

                return await directoryRepository.GetAllAsync(ct: ct);
            });

        Table table = new Table()
            .AsciiDoubleHeadBorder()
            .BorderColor(Color.SteelBlue)
            .ShowRowSeparators()
            .AddColumn("[DodgerBlue1]Id[/]")
            .AddColumn("[DodgerBlue1]Name[/]")
            .AddColumn("[DodgerBlue1]Source[/]");

        foreach (MigrationDirectoryEntity? item in migrations)
            table.AddRow(item.MigrationDirectoryId.ToString(), item.Name, item.Source.ToString("g"));

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);

        return ExitCode.Success;
    }
}
