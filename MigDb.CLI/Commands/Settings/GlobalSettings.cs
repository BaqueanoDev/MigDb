using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Settings;

internal class GlobalSettings : CommandSettings
{
    [CommandOption("--quiet")]
    [Description("Only show errors on the console.")]
    public bool Quiet { get; init; }

    [CommandOption("--verbose")]
    [Description("Show informational log output on the console.")]
    public bool Verbose { get; init; }

    [CommandOption("--debug")]
    [Description("Show all log output on the console, including full exception traces.")]
    public bool Debug { get; init; }
}
