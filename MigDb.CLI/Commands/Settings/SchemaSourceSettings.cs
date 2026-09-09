using MigDb.Core.Schema;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Settings;

internal abstract class SchemaSourceSettings : GlobalSettings
{
    [CommandOption("-t|--type <TYPE>")]
    [Description("Schema source type: common (default) or project")]
    public SchemaSourceType Type { get; init; } = SchemaSourceType.Common;

    [CommandOption("--project <PROJECT>")]
    [Description("Project name (required when type is project)")]
    public string? Project { get; init; }

    public SchemaSource Source => Type.ToSchemaSource(Project);

    public override ValidationResult Validate()
    {
        ValidationResult baseResult = base.Validate();

        if (!baseResult.Successful)
            return baseResult;

        if (Type == SchemaSourceType.Project && string.IsNullOrWhiteSpace(Project))
            return ValidationResult.Error("Project name must be specified when source is Project");

        return ValidationResult.Success();
    }
}
