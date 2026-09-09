using MigDb.CLI.Commands.Settings;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Database;

internal class DatabaseSettings : GlobalSettings, IDatabaseConnectionSetting
{
    [CommandOption("-c|--connection <CONNECTION>")]
    [Description("Connection string to use")]
    public required string Connection { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Connection))
            return ValidationResult.Error("--connection is required");

        return ValidationResult.Success();
    }
}
