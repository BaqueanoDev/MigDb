using MigDb.CLI.Utils;
using MigDb.Core.Entities;
using MigDb.Core.Migration;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Migration;

internal class MigrationValidateCommand(MigrationSelector selector, MigrationValidator validator) : AsyncCommand<MigrationValidateCommand.Settings>
{
    internal class Settings : MigrationSourceSettings
    {
        [CommandArgument(0, "<name-or-path>")]
        [Description("Directory name (from migrations config) or an absolute path")]
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
        SpectreUtils.WriteHeaderRule("Validating Directory");

        MigrationSource source = settings.Type.ToMigrationSource(settings.Project);

        MigrationDirectory directory;

        try
        {
            directory = selector.Resolve(source, settings.NameOrPath);
        }
        catch (InvalidOperationException e)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
            return ExitCode.UsageError;
        }
        catch (DirectoryNotFoundException e)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]{e.Message}[/]");
            return ExitCode.UsageError;
        }

        AnsiConsole.MarkupLineInterpolated($"Validating directory: [olive]{directory.Name}[/]");

        MigrationValidationResult validationResult = await validator.ValidateDirectoryAsync(directory, ct);

        if (validationResult.IsValid)
        {
            AnsiConsole.MarkupLine("[green]Directory is valid![/]");
        }
        else
        {
            SpectreUtils.WriteValidationFailures([validationResult]);
        }

        return validationResult.IsValid ? ExitCode.Success : ExitCode.ValidationFailed;
    }
}
