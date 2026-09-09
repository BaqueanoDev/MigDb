using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Configuration;
using MigDb.CLI.Utils;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Config;

internal class ConfigSetCommand : AsyncCommand<ConfigSetCommand.Settings>
{
    internal class Settings : GlobalSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Config key (Migration.SchemaProjectPath)")]
        public required string Key { get; set; }

        [CommandArgument(1, "<value>")]
        [Description("New value, converted by the configuration binder using the type the key declares")]
        public required string Value { get; set; }

        [CommandOption("-g|--global")]
        [Description("Write the application config in the ApplicationData folder instead of the user config")]
        public bool Global { get; set; }

        public string TargetFilePath => Global ? ConfigManager.ApplicationConfigFilePath : ConfigManager.UserConfigLocalFilePath;
        public string TargetName => ConfigUtils.GetConfigName(TargetFilePath);

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (string.IsNullOrWhiteSpace(Key))
                return ValidationResult.Error("Config key cannot be empty");

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Updating config");

        string key = ConfigUtils.NormaliseKey(settings.Key);
        ConfigOptions options = ConfigManager.LoadConfigFile(settings.TargetFilePath);
        IReadOnlyDictionary<string, string?> before = ConfigUtils.Flatten(options);

        if (!before.ContainsKey(key))
        {
            string[] underneath = [.. before.Keys.Where(k => k.StartsWith($"{key}.", StringComparison.OrdinalIgnoreCase))];

            if (underneath.Length > 0)
            {
                SpectreUtils.WriteError($"'{key}' is a section, not a value - set one of the keys underneath it");
                SpectreUtils.WriteConfigKeys(underneath);
            }
            else
            {
                SpectreUtils.WriteError($"Unknown config key '{key}'");
                SpectreUtils.WriteConfigKeys([.. before.Keys]);
            }

            return ExitCode.UsageError;
        }

        key = before.Keys.First(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

        try
        {
            IConfigurationRoot change = new ConfigurationBuilder()
                .AddInMemoryCollection([new KeyValuePair<string, string?>(key.Replace('.', ':'), settings.Value)])
                .Build();

            change.Bind(options);
        }
        catch (InvalidOperationException ex)
        {
            SpectreUtils.WriteError($"'{settings.Value}' is not a valid value for {key}");
            SpectreUtils.WriteError(ex.InnerException?.Message ?? ex.Message);

            return ExitCode.UsageError;
        }

        try
        {
            await ConfigUtils.PatchFileAsync(settings.TargetFilePath, key, options, ct);
        }
        catch (Exception ex)
        {
            SpectreUtils.WriteError($"Could not write the {settings.TargetName}: {ex.Message}");

            return ExitCode.Failure;
        }

        IReadOnlyDictionary<string, string?> after = ConfigUtils.Flatten(options);

        AnsiConsole.MarkupLineInterpolated($"File: [olive]{settings.TargetName}[/]");
        SpectreUtils.WriteSectionRule();
        AnsiConsole.MarkupLineInterpolated($"{key}: {before[key]} [grey] --> [/] [green]{after[key]}[/]");

        IReadOnlyDictionary<string, string?> resolved = ConfigUtils.Flatten(ConfigManager.LoadConfig());

        if (resolved.TryGetValue(key, out string? effective) && !string.Equals(effective, after[key], StringComparison.Ordinal))
        {
            string environmentVariable = $"{ConfigManager.EnvironmentVariablePrefix}{key.Replace(".", "__")}";

            SpectreUtils.WriteSectionRule();
            SpectreUtils.Error.MarkupLineInterpolated($"[yellow]Saved, but the effective value is still {effective} - a higher precedence source wins.[/]");

            if (Environment.GetEnvironmentVariable(environmentVariable) is not null)
                SpectreUtils.Error.MarkupLineInterpolated($"[yellow]Set by environment variable {environmentVariable}[/]");
            else if (settings.Global && File.Exists(ConfigManager.UserConfigLocalFilePath))
                SpectreUtils.Error.MarkupLine("[yellow]Set in the user config[/]");
        }

        return ExitCode.Success;
    }
}
