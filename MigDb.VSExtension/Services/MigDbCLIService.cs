using MigDb.VSExtension.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MigDb.VSExtension.Services;

internal sealed class MigDbCLIService(TraceSource logger)
{
    public const string CLIAssemblyName = "MigDb.exe";
    public const string MigrationDirectoryName = "migrations";
    public const string SchemaDirectoryName = "schema";
    public const string CommonDirectoryName = "common";
    public const string ProjectsDirectoryName = "Projects";

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string ConfigFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".migdb",
        "config.json");

    public static bool ConfigExists => File.Exists(ConfigFilePath);

    public bool IsSetup => IsMigDbCliInPath() && ConfigExists;

    public string? FindToolPath(string assemblyName)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(assemblyName);

        logger.TraceInformation("Looking for tool in path: {0}", assemblyName);

        string? envPath = Environment.GetEnvironmentVariable("PATH");

        if (envPath is null)
            return null;

        string[] pathItems = envPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? assemblyPath = pathItems
            .Select(x => Path.Combine(x, assemblyName))
            .FirstOrDefault(File.Exists);

        if (assemblyPath is null)
            return null;

        return assemblyPath;
    }

    public bool IsAssemblyInPath(string assemblyName)
    {
        return FindToolPath(assemblyName) is not null;
    }

    public bool IsMigDbCliInPath()
    {
        return IsAssemblyInPath(CLIAssemblyName);
    }

    public async Task<MigDbConfig?> ReadConfigAsync(CancellationToken cancellationToken = default)
    {
        logger.TraceInformation("Attempting to read config");

        if (!ConfigExists)
        {
            logger.TraceInformation("No config file at {0}.", ConfigFilePath);
            return null;
        }

        logger.TraceInformation("Reading config from {0}.", ConfigFilePath);

        using FileStream stream = File.OpenRead(ConfigFilePath);

        MigDbConfig? result = await JsonSerializer.DeserializeAsync<MigDbConfig>(stream, _serializerOptions, cancellationToken);

        if (result is null)
        {
            logger.TraceInformation("Config file at {0} failed to parse.", ConfigFilePath);
            return null;
        }

        return result;
    }

    public async Task<string?> GetSchemaProjectPathAsync(CancellationToken cancellationToken = default)
    {
        logger.TraceInformation("Attempting to get schema project path");

        MigDbConfig? config = await ReadConfigAsync(cancellationToken);

        if (config is null)
            return null;

        if (config.Migration.SchemaProjectPath is null)
        {
            logger.TraceInformation("SchemaProjectPath missing from config");
            return null;
        }

        return config.Migration.SchemaProjectPath;
    }

    public async Task<string?> GetCommonMigrationsPathAsync(CancellationToken cancellationToken = default)
    {
        logger.TraceInformation("Attempting to get common migration path");

        string? projPath = await GetSchemaProjectPathAsync(cancellationToken);

        if (projPath is null)
            return null;

        return Path.Combine(projPath, MigrationDirectoryName, CommonDirectoryName);
    }

    public async Task<string?> GetProjectPathAsync(CancellationToken cancellationToken = default)
    {
        logger.TraceInformation("Attempting to get project path");

        string? projPath = await GetSchemaProjectPathAsync(cancellationToken);

        if (projPath is null)
            return null;

        return Path.Combine(projPath, SchemaDirectoryName, ProjectsDirectoryName);
    }

    public async Task<string?> GetProjectMigrationPathAsync(CancellationToken cancellationToken = default)
    {
        logger.TraceInformation("Attempting to get project path");

        string? projPath = await GetSchemaProjectPathAsync(cancellationToken);

        if (projPath is null)
            return null;

        return Path.Combine(projPath, MigrationDirectoryName, ProjectsDirectoryName);
    }

    public async Task<string?> GetProjectMigrationsPathAsync(string projectName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(projectName);

        logger.TraceInformation("Attempting to get project migration path");

        string? projPath = await GetProjectMigrationPathAsync(cancellationToken);

        if (projPath is null)
            return null;

        return Path.Combine(projPath, projectName);
    }

    public async Task<IReadOnlyList<string>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        string? projectPath = await GetProjectPathAsync(cancellationToken);

        if (projectPath is null)
            return [];

        DirectoryInfo directory = new(projectPath);

        if (!directory.Exists)
            return [];

        return [.. directory.GetDirectories()
                            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(d => d.Name)
               ];
    }

    public async Task<IReadOnlyList<MigrationItem>> ListCommonMigrationsAsync(CancellationToken cancellationToken = default)
    {
        string? commonPath = await GetCommonMigrationsPathAsync(cancellationToken);

        if (commonPath is null)
            return [];

        DirectoryInfo directory = new(commonPath);

        if (!directory.Exists)
            return [];

        return [.. directory.GetDirectories()
                            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(d => new MigrationItem(d.Name, d.FullName))
               ];
    }

    public async Task<IReadOnlyList<MigrationItem>> ListProjectMigrationsAsync(string projectName, CancellationToken cancellationToken = default)
    {
        string? projectPath = await GetProjectMigrationsPathAsync(projectName, cancellationToken);

        if (projectPath is null)
            return [];

        DirectoryInfo directory = new(projectPath);

        if (!directory.Exists)
            return [];

        return [.. directory.GetDirectories()
                            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(d => new MigrationItem(d.Name, d.FullName))
               ];
    }

    public async Task<CLIRunResult> RunCommandAsync(string arguments, CancellationToken cancellationToken, Action<string>? onOutputLine = null)
    {
        if (!IsMigDbCliInPath())
            return new CLIRunResult(arguments, -1, $"{CLIAssemblyName} was not found on PATH.");

        ProcessStartInfo pInfo = new()
        {
            FileName = CLIAssemblyName,
            Arguments = $"--no-logo {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // hurts that we have to do this.
        // VS only exposes whatever runtime it ships with
        // cli is built with .net 10
        // need to remove env vars and add machine vars

        string[] envVars = [
                            "DOTNET_ROOT",
                            "DOTNET_ROOT(x86)",
                            "DOTNET_ROOT_X86",
                            "DOTNET_ROOT_X64",
                            "DOTNET_ROOT_ARM64",
                            "DOTNET_HOST_PATH",
                            "DOTNET_MULTILEVEL_LOOKUP"
                            ];

        foreach (string variable in envVars)
            pInfo.Environment.Remove(variable);

        string machineRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet");

        string dotnetRoot = Path.Combine(machineRoot, "dotnet.exe");

        // only add machine env vars if they exist
        if (File.Exists(dotnetRoot))
        {
            pInfo.Environment["DOTNET_ROOT"] = machineRoot;
            pInfo.Environment["DOTNET_ROOT_X64"] = machineRoot;
        }

        using Process? process = Process.Start(pInfo);

        if (process is null)
            return new CLIRunResult(arguments, -1, $"Failed to start {CLIAssemblyName}.");

        StringBuilder output = new();

        Task stdOutPump = PumpAsync(process.StandardOutput, output, onOutputLine, cancellationToken);
        Task stdErrPump = PumpAsync(process.StandardError, output, onOutputLine, cancellationToken);

        await Task.WhenAll(stdOutPump, stdErrPump);
        await process.WaitForExitAsync(cancellationToken);

        return new CLIRunResult(arguments, process.ExitCode, output.ToString().TrimEnd());
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder output, Action<string>? onOutputLine, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lock (output)
                output.AppendLine(line);

            onOutputLine?.Invoke(line);
        }
    }
}
