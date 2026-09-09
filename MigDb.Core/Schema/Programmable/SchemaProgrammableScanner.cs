using MigDb.Core.Migration.Validation;
using MigDb.Core.Utils;
using MigDb.Core.Utils.ScriptDom;
using Microsoft.Extensions.Logging;

namespace MigDb.Core.Schema.Programmable;

/// <summary>
/// Handles IO for programmable files
/// </summary>
/// <param name="logger"></param>
/// <param name="pathResolver"></param>
/// <param name="validator"></param>
public sealed class SchemaProgrammableScanner(ILogger<SchemaProgrammableScanner> logger, SchemaPathResolver pathResolver, MigrationValidator validator)
{
    public static readonly string[] FolderWhitelist =
    [
        "StoredProcedures",
        "Views",
        "Functions",
        "Triggers",
    ];

    public async Task<IReadOnlyList<SchemaProgrammableFile>> LoadBySourceAsync(SchemaSource source, CancellationToken ct = default)
    {
        return await LoadFromRootAsync(pathResolver.ResolveSchemaSourceRoot(source), source, ct);
    }

    /// <summary>
    /// Scans the programmable folders under an arbitrary schema source root, rather than the
    /// configured one. Lets a revision extracted out of git be scanned the same way as the
    /// working tree.
    /// </summary>
    /// <param name="rootPath">Schema source root to scan under</param>
    /// <param name="source">Source the files are attributed to</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Every programmable found under <paramref name="rootPath"/></returns>
    public async Task<IReadOnlyList<SchemaProgrammableFile>> LoadFromRootAsync(string rootPath, SchemaSource source, CancellationToken ct = default)
    {
        logger.LogDebug("Scanning folders for programmable objects at: {Path}", rootPath);

        List<SchemaProgrammableFile> result = [];

        foreach (string w in FolderWhitelist)
        {
            string fPath = Path.Combine(rootPath, w);
            DirectoryInfo dInfo = new(fPath);

            if (!dInfo.Exists)
            {
                logger.LogDebug("Programmable folder not found, skipping: {Folder}", dInfo.FullName);
                continue;
            }

            FileInfo[] files = dInfo.GetFiles("*.sql", SearchOption.AllDirectories);

            foreach (FileInfo f in files)
            {
                SchemaProgrammableFile p = await LoadFileAsync(f, source, ct);
                result.Add(p);
            }
        }

        logger.LogDebug("Loaded {Count} programmable(s) from {Path}", result.Count, rootPath);

        return result;
    }

    public async Task<SchemaProgrammableFile> LoadFileAsync(FileInfo file, SchemaSource source, CancellationToken ct = default)
    {
        logger.LogDebug("Loading programmable file {File}", file.FullName);

        string content = await File.ReadAllTextAsync(file.FullName, ct);
        string script = StringUtils.Normalise(content);

        MigrationValidationResult validationErrors = validator.ValidateProgrammableScript(script);

        if (!validationErrors.IsValid)
        {
            foreach (MigrationValidationError e in validationErrors.Errors)
                logger.LogWarning("Programmable '{File}' failed validation: {error}", file.FullName, e);

            throw new InvalidOperationException($"Programmable '{file.FullName}' contains one or more validation errors");
        }

        SchemaProgrammableDefinition? definition = ScriptDomUtils.GetProgrammable(script);

        if (definition is null)
            throw new InvalidOperationException($"Unable to identify object in script");

        SchemaObject p = definition.Object;

        if (definition.Exclusion != SchemaProgrammableExclusion.None)
            logger.LogDebug("Programmable {Object} is {Exclusion} - the deploy sweep will leave it alone", p.Name, definition.Exclusion);

        byte[] hash = HashUtils.SHA256(script);

        SchemaProgrammableFile result = new(p.Schema, p.Name, source, p.Kind, file, script, hash, definition.Exclusion);

        return result;
    }

    public async Task<IReadOnlyList<SchemaProgrammableFile>> LoadCommonWithProjectAsync(SchemaSource source, CancellationToken ct = default)
    {
        List<SchemaProgrammableFile> result = [.. await LoadBySourceAsync(new SchemaSource.Common(), ct)];

        if (source is SchemaSource.Project)
            result.AddRange(await LoadBySourceAsync(source, ct));

        return result;
    }
}