namespace MigDb.VSExtension.Models;

internal sealed record CLIRunResult(string Arguments, int ExitCode, string Output);