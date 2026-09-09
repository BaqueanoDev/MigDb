namespace MigDb.CLI.Utils;

internal static class ExitCode
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;
    public const int ValidationFailed = 3;
    public const int NotInitialised = 4;
    public const int DriftDetected = 5;
}
