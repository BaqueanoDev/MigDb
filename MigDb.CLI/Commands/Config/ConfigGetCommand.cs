using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Configuration;
using MigDb.CLI.Utils;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigDb.CLI.Commands.Config;

internal class ConfigGetCommand(ConfigOptions config) : AsyncCommand<ConfigGetCommand.Settings>
{
    internal class Settings : GlobalSettings
    {
        [CommandArgument(0, "[key]")]
        [Description("Config key (Migration.SchemaProjectPath). Name a section for just that section, or omit for everything")]
        public string? Key { get; set; }

        [CommandOption("--path")]
        [Description("List the config files the CLI reads instead of the settings themselves")]
        public bool Path { get; set; }

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (Path && !string.IsNullOrWhiteSpace(Key))
                return ValidationResult.Error("--path lists config files, so it cannot be combined with a key");

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        if (settings.Path)
        {
            SpectreUtils.WriteHeaderRule("Config files");
            AnsiConsole.MarkupLineInterpolated($"[grey]Lowest precedence first and {ConfigManager.EnvironmentVariablePrefix}* variables beat all of them[/]");
            SpectreUtils.WriteSectionRule();

            string[] paths = ConfigManager.ConfigFilePaths;
            int width = paths.Max(p => ConfigUtils.GetConfigName(p).Length);

            foreach (string path in paths)
            {
                string name = ConfigUtils.GetConfigName(path).PadRight(width);
                string missing = string.Empty;

                if (!File.Exists(path))
                    missing = " [grey](missing)[/]";

                AnsiConsole.MarkupLine($"[olive]{Markup.Escape(name)}[/]  {Markup.Escape(path)}{missing}");
            }

            return ExitCode.Success;
        }

        SpectreUtils.WriteHeaderRule("Reading config");

        IReadOnlyDictionary<string, string?> valueLookup = ConfigUtils.Flatten(config);
        IReadOnlyDictionary<string, string> sourcesLookup;

        try
        {
            sourcesLookup = await ConfigUtils.FlattenSourcesAsync(ConfigManager.ConfigFilePaths, ct);
        }
        catch (Exception ex)
        {
            SpectreUtils.WriteError($"Could not read the config files: {ex.Message}");

            return ExitCode.Failure;
        }

        string[] keys = [.. valueLookup.Keys];

        if (!string.IsNullOrWhiteSpace(settings.Key))
        {
            string wanted = ConfigUtils.NormaliseKey(settings.Key);

            keys = [.. valueLookup.Keys.Where(k =>
                string.Equals(k, wanted, StringComparison.OrdinalIgnoreCase)
                || k.StartsWith($"{wanted}.", StringComparison.OrdinalIgnoreCase))];
        }

        if (keys.Length == 0)
        {
            SpectreUtils.WriteError($"Unknown config key '{settings.Key}'");
            SpectreUtils.WriteConfigKeys([.. valueLookup.Keys]);

            return ExitCode.UsageError;
        }

        Table table = new Table()
            .AsciiDoubleHeadBorder()
            .BorderColor(Color.SteelBlue)
            .AddColumn(new TableColumn("[DodgerBlue1]Key[/]").NoWrap())
            .AddColumn(new TableColumn("[DodgerBlue1]Source[/]").NoWrap())
            .AddColumn("[DodgerBlue1]Value[/]");

        foreach (string key in keys)
        {
            string source = "default";

            if (sourcesLookup.TryGetValue(key, out string? found))
                source = found;

            table.AddRow(
                Markup.Escape(key),
                Markup.Escape(source),
                Markup.Escape(Describe(valueLookup[key])));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);

        return ExitCode.Success;
    }

    private static string Describe(string? value)
    {
        if (value is null)
            return "(null)";

        if (value.Length == 0)
            return "(empty)";

        return value;
    }
}
