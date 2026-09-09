using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationListCommand(MigrationOptions migrationOptions, MigrationDirectoryFactory directoryFactory, MigrationValidator validator, MigrationDirectoryRepository directoryRepo) : AsyncCommand<MigrationListCommand.Settings>
{
    internal class Settings : MigrationSourceSettings, IDatabaseConnectionSetting
    {
        [CommandOption("-c|--connection <CONNECTION>")]
        [Description("Connection string to use")]
        public string? Connection { get; init; }

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (Connection is not null && string.IsNullOrWhiteSpace(Connection))
                return ValidationResult.Error("Invalid connection string");

            return ValidationResult.Success();
        }
    }

    internal enum MigrationListItemStatus
    {
        Valid,
        Applied,
        Pending,
        Invalid,
        Orphaned,
        Drifted
    }

    internal sealed class MigrationListItem
    {
        public required string Name { get; init; }
        public required string StatusText { get; init; }
        public MigrationListItemStatus Status { get; init; }
        public IReadOnlyList<MigrationValidationError> Errors { get; init; } = [];
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        List<MigrationListItem>? items = await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("gold1"))
            .StartAsync<List<MigrationListItem>?>("[steelblue1]Listing available migrations...[/]", async ctx =>
            {
                SpectreUtils.WriteHeaderRule("Listing available migrations");

                if (settings.Connection is not null)
                    AnsiConsole.WriteLine("Connection string detected, will compare with database");

                MigrationSource source = settings.Type.ToMigrationSource(settings.Project);

                List<MigrationDirectory> migrations = directoryFactory.GetMigrationDirectories(source);

                if (migrations.Count == 0)
                {
                    AnsiConsole.MarkupLineInterpolated($"No migrations found under: {migrationOptions.SchemaProjectPath}");
                    return null;
                }

                AnsiConsole.MarkupLineInterpolated($"Found ({migrations.Count})");

                List<MigrationListItem> items = new(migrations.Count);

                if (settings.Connection is null)
                {
                    foreach (MigrationDirectory m in migrations)
                    {
                        MigrationValidationResult validation = await validator.ValidateDirectoryAsync(m, ct);

                        bool isValid = validation.IsValid;

                        items.Add(new MigrationListItem
                        {
                            Name = m.Name,
                            Status = isValid ? MigrationListItemStatus.Valid : MigrationListItemStatus.Invalid,
                            StatusText = isValid ? "Valid" : "Invalid",
                            Errors = [.. validation.AllErrors.Distinct()],
                        });
                    }
                }
                else
                {
                    IReadOnlyList<MigrationDirectoryEntity> dbDirectories = await directoryRepo.GetAllAsync(MigrationSource.ToMigrationSourceType(source), ct: ct);

                    foreach (MigrationDirectory m in migrations)
                    {
                        MigrationValidationResult validation = await validator.ValidateDirectoryAsync(m, ct);

                        bool isValid = validation.IsValid;

                        MigrationDirectoryEntity? applied = dbDirectories.FirstOrDefault(x => x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));

                        bool isDrifted = applied is not null && await validator.HasDriftedAsync(m, applied.Hash, ct);
                        string dbStatus = applied is not null ? "Applied" : "Pending";

                        string statusText;
                        MigrationListItemStatus status;

                        if (isDrifted)
                        {
                            status = MigrationListItemStatus.Drifted;
                            statusText = isValid ? "Drifted" : "Drifted (Invalid)";
                        }
                        else if (!isValid)
                        {
                            status = MigrationListItemStatus.Invalid;
                            statusText = $"{dbStatus} (Invalid)";
                        }
                        else if (applied is not null)
                        {
                            status = MigrationListItemStatus.Applied;
                            statusText = dbStatus;
                        }
                        else
                        {
                            status = MigrationListItemStatus.Pending;
                            statusText = dbStatus;
                        }

                        items.Add(new MigrationListItem
                        {
                            Name = m.Name,
                            Status = status,
                            StatusText = statusText,
                            Errors = [.. validation.AllErrors.Distinct()],
                        });
                    }

                    foreach (MigrationDirectoryEntity d in dbDirectories)
                    {
                        if (migrations.Any(y => y.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        items.Add(new MigrationListItem
                        {
                            Name = d.Name,
                            Status = MigrationListItemStatus.Orphaned,
                            StatusText = "Orphaned",
                        });
                    }
                }

                return items;
            });

        if (items is null)
            return ExitCode.Success;

        Table table = new Table()
            .AsciiDoubleHeadBorder()
            .BorderColor(Color.SteelBlue)
            .ShowRowSeparators()
            .AddColumn("[DodgerBlue1]Migration[/]")
            .AddColumn("[DodgerBlue1]Status[/]")
            .AddColumn("[DodgerBlue1]Issues[/]");

        List<MigrationListItem> itemsOrdered = items.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

        foreach (MigrationListItem? item in itemsOrdered)
        {
            string color = GetItemStatusColor(item);

            List<string> issueList = [.. item.Errors.Select(MigrationExtensions.Describe)];

            string issues = issueList.Count == 0 ? "-" : string.Join(", ", issueList);

            table.AddRow($"[{color}]{item.Name}[/]", $"[{color}]{item.StatusText}[/]", $"[{color}]{issues}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);

        return ExitCode.Success;
    }

    public string GetItemStatusColor(MigrationListItem item)
    {
        return item.Status switch
        {
            MigrationListItemStatus.Valid => "green",
            MigrationListItemStatus.Applied => "green",
            MigrationListItemStatus.Pending => "gold1",
            MigrationListItemStatus.Orphaned => "red",
            MigrationListItemStatus.Invalid => "red",
            MigrationListItemStatus.Drifted => "red",
            _ => "gold1",
        };
    }
}
