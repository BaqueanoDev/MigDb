using System.Reflection;

namespace MigDb.CLI.Utils;

internal static class AssemblyUtils
{
    public static string GetAssemblyVersion()
    {
        Assembly assembly = typeof(Program).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
