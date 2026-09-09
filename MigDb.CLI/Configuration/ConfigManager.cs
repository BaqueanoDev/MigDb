using MigDb.CLI.Configuration.Logging;
using MigDb.CLI.Configuration.Logging.Sinks;
using MigDb.CLI.Utils;
using MigDb.Core.Options;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Text.Json;

namespace MigDb.CLI.Configuration;

internal static class ConfigManager
{
    public const string EnvironmentVariablePrefix = "MIGDB_";

    public static string? SchemaProjectPathOverride { get; set; }

    public static string DataDirectoryName { get; private set; } = "migdb";

    public static string ApplicationDataDirectoryName { get; private set; } = DataDirectoryName;

    public static string UserDataDirectoryName { get; private set; } = $".{DataDirectoryName}";
    public static string ConfigFileName { get; private set; } = "config.json";
    public static string AssemblyConfigFileName { get; private set; } = "appsettings.json";
    public static string MigrationDirectoryName { get; private set; } = "migrations";

    public static string AssemblyConfigFilePath => Path.Combine(
        AppContext.BaseDirectory,
        AssemblyConfigFileName);

    public static string ApplicationConfigFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ApplicationDataDirectoryName,
        ConfigFileName);

    public static string UserConfigLocalFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        UserDataDirectoryName,
        ConfigFileName);

    public static string[] ConfigFilePaths => [
        AssemblyConfigFilePath,
        ApplicationConfigFilePath,
        UserConfigLocalFilePath
    ];

    public static string LogFilePath => Path.Combine(
       Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
       DataDirectoryName,
       "logs");

    public static string MigrationDirectoryPath => Path.Combine(
       Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
       UserDataDirectoryName,
       MigrationDirectoryName);

    public static LoggingLevelSwitch ConsoleLogLevelSwitch { get; } = new(LogEventLevel.Warning);
    public static LoggingLevelSwitch FileLogLevelSwitch { get; } = new(LogEventLevel.Verbose);

    public static bool AssemblyConfigExists() => File.Exists(AssemblyConfigFilePath);

    public static bool ApplicationConfigExists() => File.Exists(ApplicationConfigFilePath);

    public static bool UserConfigExists() => File.Exists(UserConfigLocalFilePath);

    public static ConfigOptions GetDefaultOptions()
    {
        ConfigOptions result = new()
        {

            Database = new DatabaseOptions(),
            Migration = new MigrationOptions
            {
                SchemaProjectPath = MigrationDirectoryPath,
            },
            Logging = new LoggingOptions
            {
                ConsoleLevel = LogEventLevel.Warning,
                FileLevel = LogEventLevel.Verbose
            }
        };

        return result;
    }

    public static ConfigOptions LoadConfig()
    {
        ConfigOptions options = GetDefaultOptions();
        Dictionary<string, string?> commandLine = [];

        if (SchemaProjectPathOverride is not null)
            commandLine["Migration:SchemaProjectPath"] = SchemaProjectPathOverride;

        IConfigurationRoot configBuilder = new ConfigurationBuilder()
            .AddJsonFile(AssemblyConfigFilePath, optional: true, reloadOnChange: false)
            .AddJsonFile(ApplicationConfigFilePath, optional: true, reloadOnChange: false)
            .AddJsonFile(UserConfigLocalFilePath, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(EnvironmentVariablePrefix)
            .AddInMemoryCollection(commandLine)
            .Build();

        configBuilder.Bind(options);

        return options;
    }

    public static ConfigOptions LoadConfigFile(string filePath)
    {
        ConfigOptions options = GetDefaultOptions();

        IConfigurationRoot configBuilder = new ConfigurationBuilder()
            .AddJsonFile(filePath, optional: true, reloadOnChange: false)
            .Build();

        configBuilder.Bind(options);

        return options;
    }

    public static bool WriteConfig(ConfigOptions options)
    {
        string defaultData = JsonSerializer.Serialize(options, ConfigUtils.SerializerOptions);
        string? directory = Path.GetDirectoryName(ApplicationConfigFilePath);

        if (string.IsNullOrEmpty(directory))
            throw new ApplicationException($"Config path does not name a directory: {ApplicationConfigFilePath}");

        Directory.CreateDirectory(directory);
        File.WriteAllText(ApplicationConfigFilePath, defaultData);

        return true;
    }

    public static LoggerConfiguration ConfigureLogging(LoggerConfiguration cfg, LoggingOptions logging)
    {
        string filePathTemplate = Path.Join(LogFilePath, "migdb-.log");
        FileLogLevelSwitch.MinimumLevel = logging.FileLevel;
        ConsoleLogLevelSwitch.MinimumLevel = logging.ConsoleLevel;

        return cfg
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .Destructure.With(new ConnectionStringDestructuringPolicy())
            .WriteTo.File(
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                path: filePathTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                rollOnFileSizeLimit: true,
                levelSwitch: FileLogLevelSwitch
            )
            .WriteTo.Spectre(levelSwitch: ConsoleLogLevelSwitch);
    }
}
