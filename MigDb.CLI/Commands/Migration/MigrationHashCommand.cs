using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Migration;
using MigDb.Core.Migration.Manifest;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationHashCommand(MigrationOptions migrationOptions, MigrationSelector selector, MigrationHasher hasher) : AsyncCommand<MigrationHashCommand.Settings>
{
    internal class Settings : MigrationSourceSettings
    {
        [CommandArgument(0, "<name-or-path>")]
        [Description("Directory name (from migrations config), or an absolute path to a directory or sql file")]
        public required string NameOrPath { get; set; }

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (string.IsNullOrWhiteSpace(NameOrPath))
                return ValidationResult.Error("Name or path cannot be empty");

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        MigrationSource source = settings.Type.ToMigrationSource(settings.Project);

        string[] pathTokens = settings.NameOrPath.Split(Path.DirectorySeparatorChar);

        if (pathTokens.Length == 1)
        {
            SpectreUtils.WriteHeaderRule("Hashing Directory");

            string fName = pathTokens[^1];

            AnsiConsole.WriteLine("Only directory name provided");
            SpectreUtils.WriteSectionRule();
            AnsiConsole.WriteLine("Checking config for migration directory");

            if (string.IsNullOrWhiteSpace(migrationOptions.SchemaProjectPath))
            {
                SpectreUtils.Error.MarkupLine("[red]Migration directory path missing from config... Setup config or provide full path[/]");
                return ExitCode.UsageError;
            }

            MigrationDirectory migrationDirectory = selector.Get(source, fName);

            if (!migrationDirectory.Info.Exists)
            {
                SpectreUtils.Error.MarkupLine("[red]Migration directory doesnt exists...[/]");
                return ExitCode.UsageError;
            }

            await HashDirectoryAsync(migrationDirectory, settings, ct);
            return ExitCode.Success;
        }

        if (Directory.Exists(settings.NameOrPath))
        {
            MigrationDirectory migrationDirectory = new(settings.NameOrPath, source);

            SpectreUtils.WriteHeaderRule("Hashing Directory");
            await HashDirectoryAsync(migrationDirectory, settings, ct);
            return ExitCode.Success;
        }

        if (File.Exists(settings.NameOrPath))
        {
            SpectreUtils.WriteHeaderRule("Hashing File");
            await HashSingleFileAsync(settings.NameOrPath, ct);
            return ExitCode.Success;
        }

        SpectreUtils.Error.MarkupLine("[red]No file or directory found at provided path...[/]");
        return ExitCode.UsageError;
    }

    private async Task HashDirectoryAsync(MigrationDirectory directory, Settings settings, CancellationToken ct)
    {
        AnsiConsole.MarkupLineInterpolated($"Hashing directory: [olive]{settings.NameOrPath}[/]");

        MigrationDirectoryHashResult dirResult = await hasher.HashDirectoryAsync(directory, ct);

        AnsiConsole.MarkupLineInterpolated($"Directory Hash: {Convert.ToHexString(dirResult.DirectoryHash)}");
        SpectreUtils.WriteSectionRule();

        foreach (MigrationFileHashResult fileResult in dirResult.Files.Values)
        {
            AnsiConsole.MarkupLineInterpolated($"File: {fileResult.Name}");
            AnsiConsole.MarkupLineInterpolated($"Hash: {Convert.ToHexString(fileResult.Hash)}");
            SpectreUtils.WriteSectionRule();
        }
    }

    private async Task HashSingleFileAsync(string filePath, CancellationToken ct)
    {
        FileInfo f = new(filePath);

        AnsiConsole.MarkupLineInterpolated($"Hashing file: [olive]{f.Name}[/]");
        SpectreUtils.WriteSectionRule();

        string fileHash = Convert.ToHexString((await hasher.HashFileAsync(f, ct)).Hash);

        AnsiConsole.MarkupLineInterpolated($"File: {f.Name}");
        AnsiConsole.MarkupLineInterpolated($"Hash: {fileHash}");
        SpectreUtils.WriteSectionRule();
    }
}
