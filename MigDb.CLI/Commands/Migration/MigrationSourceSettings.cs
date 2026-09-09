using MigDb.CLI.Commands.Settings;
using MigDb.Core.Entities;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Migration;

internal abstract class MigrationSourceSettings : GlobalSettings
{
    [CommandOption("-t|--type <TYPE>")]
    [Description("Migration source type: common (default) or project")]
    public MigrationSourceType Type { get; init; } = MigrationSourceType.Common;

    [CommandOption("--project <PROJECT>")]
    [Description("Project name (required when type is project)")]
    public string? Project { get; init; }

    public override ValidationResult Validate()
    {
        if (Type == MigrationSourceType.Project && string.IsNullOrWhiteSpace(Project))
            return ValidationResult.Error("Project name must be specified when source is Project");

        // External is derived from an out-of-root path, it has no source root to resolve
        if (Type == MigrationSourceType.External)
            return ValidationResult.Error("Source type 'external' cannot be selected, pass a full path instead");

        return ValidationResult.Success();
    }
}
