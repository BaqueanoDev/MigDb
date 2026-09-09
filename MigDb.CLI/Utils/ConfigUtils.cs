using MigDb.CLI.Configuration;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MigDb.CLI.Utils;

internal static class ConfigUtils
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string NormaliseKey(string key) => key.Replace(':', '.');

    public static string GetConfigName(string filePath)
    {
        if (string.Equals(filePath, ConfigManager.AssemblyConfigFilePath, StringComparison.OrdinalIgnoreCase))
            return "assembly config";

        if (string.Equals(filePath, ConfigManager.ApplicationConfigFilePath, StringComparison.OrdinalIgnoreCase))
            return "application config";

        if (string.Equals(filePath, ConfigManager.UserConfigLocalFilePath, StringComparison.OrdinalIgnoreCase))
            return "user config";

        return filePath;
    }

    public static IReadOnlyDictionary<string, string?> Flatten(ConfigOptions options)
    {
        Dictionary<string, string?> result = new(StringComparer.OrdinalIgnoreCase);

        Flatten(JsonSerializer.SerializeToNode(options, SerializerOptions), string.Empty, result);

        return result;
    }

    public static async Task<IReadOnlyDictionary<string, string>> FlattenSourcesAsync(IReadOnlyList<string> ConfigFilePaths, CancellationToken ct = default)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in ConfigFilePaths)
        {
            IReadOnlyDictionary<string, string?> stored = await FlattenFileAsync(file, ct);

            foreach (string key in stored.Keys)
                result[key] = GetConfigName(file);
        }

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string name = entry.Key.ToString() ?? string.Empty;

            if (!name.StartsWith(ConfigManager.EnvironmentVariablePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            result[name[ConfigManager.EnvironmentVariablePrefix.Length..].Replace("__", ".")] = "environment";
        }

        if (ConfigManager.SchemaProjectPathOverride is not null)
            result["Migration.SchemaProjectPath"] = "command line";

        return result;
    }

    public static async Task<IReadOnlyDictionary<string, string?>> FlattenFileAsync(string filePath, CancellationToken ct = default)
    {
        Dictionary<string, string?> result = new(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(filePath))
            return result;

        string json = await File.ReadAllTextAsync(filePath, ct);

        if (string.IsNullOrWhiteSpace(json))
            return result;

        Flatten(JsonNode.Parse(json), string.Empty, result);

        return result;
    }

    public static async Task PatchFileAsync(string filePath, string key, ConfigOptions options, CancellationToken ct = default)
    {
        string[] segments = NormaliseKey(key).Split('.');
        JsonNode? value = JsonSerializer.SerializeToNode(options, SerializerOptions);

        foreach (string segment in segments)
            value = value?[segment];

        if (value is null)
            throw new ApplicationException($"There is no value to write for {key}");

        JsonObject root = [];

        if (File.Exists(filePath))
        {
            string json = await File.ReadAllTextAsync(filePath, ct);

            if (!string.IsNullOrWhiteSpace(json) && JsonNode.Parse(json) is JsonObject parsed)
                root = parsed;
        }

        JsonObject parent = root;

        foreach (string segment in segments[..^1])
        {
            if (parent[segment] is not JsonObject section)
            {
                section = [];
                parent[segment] = section;
            }

            parent = section;
        }

        parent[segments[^1]] = value.DeepClone();

        string? directory = Path.GetDirectoryName(filePath);

        if (string.IsNullOrEmpty(directory))
            throw new ApplicationException($"Config path does not name a directory: {filePath}");

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(filePath, root.ToJsonString(SerializerOptions), ct);
    }

    private static void Flatten(JsonNode? node, string prefix, Dictionary<string, string?> result)
    {
        if (node is JsonObject section)
        {
            foreach (KeyValuePair<string, JsonNode?> child in section)
            {
                string key;

                if (prefix.Length == 0)
                    key = child.Key;
                else
                    key = $"{prefix}.{child.Key}";

                Flatten(child.Value, key, result);
            }

            return;
        }

        if (prefix.Length > 0)
            result[prefix] = node?.ToString();
    }
}
