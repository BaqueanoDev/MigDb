using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Repositories;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Database;

internal class DatabaseRevertListCommand(MigrationRevertRepository revertRepository) : AsyncCommand<DatabaseRevertListCommand.Settings>
{
    internal class Settings : DatabaseSettings
    {
        [CommandOption("-n|--take <COUNT>")]
        [Description("How many of the most recent entries to show (default: 50). Pass 0 for all")]
        public int Take { get; init; } = 50;

        [CommandOption("--name <NAME>")]
        [Description("Only show entries for a migration directory or programmable with this name")]
        public string? Name { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        IReadOnlyList<MigrationRevertEntity> reverts = await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("gold1"))
            .StartAsync("[steelblue1]Listing reverts...[/]", async ctx =>
            {
                SpectreUtils.WriteHeaderRule("Listing reverts");

                if (!string.IsNullOrWhiteSpace(settings.Name))
                    return await revertRepository.GetByNameAsync(settings.Name, ct: ct);

                if (settings.Take <= 0)
                    return await revertRepository.GetAllAsync(ct: ct);

                IReadOnlyList<MigrationRevertEntity> latest = await revertRepository.GetLatestAsync(settings.Take, ct: ct);

                return [.. latest.Reverse()];
            });

        if (reverts.Count == 0)
        {
            AnsiConsole.MarkupLine("[olive]Nothing has been reverted[/]");
            return ExitCode.Success;
        }

        Table table = new Table()
            .AsciiDoubleHeadBorder()
            .BorderColor(Color.SteelBlue)
            .ShowRowSeparators()
            .AddColumn("[DodgerBlue1]Id[/]")
            .AddColumn("[DodgerBlue1]Reverted[/]")
            .AddColumn("[DodgerBlue1]By[/]")
            .AddColumn("[DodgerBlue1]Kind[/]")
            .AddColumn("[DodgerBlue1]Name[/]")
            .AddColumn("[DodgerBlue1]Source[/]")
            .AddColumn("[DodgerBlue1]Run[/]")
            .AddColumn("[DodgerBlue1]Target[/]")
            .AddColumn("[DodgerBlue1]Hash[/]");

        foreach (MigrationRevertEntity item in reverts)
        {
            table.AddRow(
                item.MigrationRevertId.ToString(),
                item.RevertedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                item.RevertedBy,
                item.Kind.ToString("g"),
                item.Name,
                item.SourceName ?? item.Source.ToString("g"),
                item.MigrationRunId?.ToString() ?? "-",
                item.TargetRevision ?? "-",
                DescribeHash(item));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);

        return ExitCode.Success;
    }

    private static string DescribeHash(MigrationRevertEntity revert)
    {
        string removed = Short(revert.Hash);

        if (revert.Kind != MigrationRevertKind.Programmable)
            return removed;

        return revert.PreviousHash is null ? $"{removed} -> dropped" : $"{removed} -> {Short(revert.PreviousHash)}";
    }

    private static string Short(byte[]? hash)
    {
        return hash is null || hash.Length == 0 ? "-" : Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
