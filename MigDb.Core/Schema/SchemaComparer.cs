using MigDb.Core.Schema.Programmable;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Compare;
using Microsoft.SqlServer.Dac.Model;

namespace MigDb.Core.Schema;

public sealed class SchemaComparer(ILogger<SchemaComparer> logger, SchemaProgrammableScanner scanner, Options.DatabaseOptions dbOptions)
{
    public async Task<SchemaProgrammableComparisonResult> CompareProgrammableFilesAsync(string sourceRoot, string targetRoot, SchemaSource source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);

        logger.LogDebug("Comparing programmable files {SourceRoot} -> {TargetRoot}", sourceRoot, targetRoot);

        IReadOnlyList<SchemaProgrammableFile> sourceProgrammables = await scanner.LoadFromRootAsync(sourceRoot, source, ct);
        IReadOnlyList<SchemaProgrammableFile> targetProgrammables = await scanner.LoadFromRootAsync(targetRoot, source, ct);

        return await CompareProgrammableFilesAsync(sourceProgrammables, targetProgrammables, ct);
    }

    public async Task<SchemaProgrammableComparisonResult> CompareProgrammableFilesAsync(IReadOnlyList<SchemaProgrammableFile> lFiles, IReadOnlyList<SchemaProgrammableFile> rFiles, CancellationToken ct = default)
    {
        HashSet<string> sourceObjects = [.. lFiles.Select(p => $"{p.Type}.{p.FullName}")];
        HashSet<string> targetObjects = [.. rFiles.Select(p => $"{p.Type}.{p.FullName}")];

        IReadOnlyList<SchemaProgrammableFile> added = [.. lFiles.Where(p => !targetObjects.Contains($"{p.Type}.{p.FullName}"))];

        IReadOnlyList<SchemaProgrammableFile> changed = [.. lFiles
            .Where(p => targetObjects.Contains($"{p.Type}.{p.FullName}"))
            .ExceptBy(rFiles.Select(p => $"{p.Type}.{p.FullName}.{Convert.ToHexString(p.Hash)}"), p => $"{p.Type}.{p.FullName}.{Convert.ToHexString(p.Hash)}")];

        IReadOnlyList<SchemaProgrammableFile> removed = [.. rFiles.Where(p => !sourceObjects.Contains($"{p.Type}.{p.FullName}"))];


        logger.LogDebug("Found {Added} added, {Changed} changed and {Removed} removed programmable(s)", added.Count, changed.Count, removed.Count);

        return new SchemaProgrammableComparisonResult(added, changed, removed);
    }

    /// <summary>
    /// Uses DacFx schema comparer to compare two dacpacs
    /// </summary>
    /// <param name="currentDacpac">Source Dacpac</param>
    /// <param name="targetDacpac">Target Dacpac</param>
    /// <param name="excludedObjects">Object types to leave out of the comparison</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Schema comparison result</returns>
    public SchemaComparisonResult CompareSchema(string currentDacpac, string targetDacpac, IReadOnlyList<ObjectType>? excludedObjects = null, bool blockOnDataLoss = true, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDacpac);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDacpac);

        // this is feeling very web dev...
        excludedObjects ??= [];

        logger.LogDebug("Comparing schemas {SchemaA} -> {SchemaB}", currentDacpac, targetDacpac);

        foreach (ObjectType e in excludedObjects)
        {
            logger.LogDebug("Excluding: {Object}", e);
        }

        SchemaCompareDacpacEndpoint c = new(currentDacpac);
        SchemaCompareDacpacEndpoint t = new(targetDacpac);

        SchemaComparison comparer = new(c, t)
        {
            Options = {
                ExcludeObjectTypes = [.. excludedObjects],
                IgnoreColumnOrder = true,
                ScriptRefreshModule = false,
                DisableAndReenableDdlTriggers = false,
                IncludeTransactionalScripts = false,
                BlockOnPossibleDataLoss = blockOnDataLoss
            }
        };

        SchemaComparisonResult result = comparer.Compare(ct);

        return result;
    }

    public Task<SchemaDriftReport> CompareToDatabaseAsync(string dacpacPath, string connectionString, CancellationToken ct = default)
    {
        return Task.Run(() => CompareToDatabase(dacpacPath, connectionString, ct), ct);
    }

    public SchemaDriftReport CompareToDatabase(string dacpacPath, string connectionString, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dacpacPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        SchemaCompareDacpacEndpoint project = new(dacpacPath);
        SchemaCompareDatabaseEndpoint database = new(connectionString);

        logger.LogDebug("Comparing {Dacpac} against database {Database}", dacpacPath, database.DatabaseName);

        SchemaComparison comparer = new(project, database)
        {
            Options = {
                ExcludeObjectTypes = [.. SchemaObjectTypes.Unmanaged],
                AllowIncompatiblePlatform = true,
                IncludeCompositeObjects = true,
                CommandTimeout = dbOptions.CommandTimeoutSeconds,
                IgnoreColumnOrder = true,
                IgnorePermissions = true,
                IgnoreRoleMembership = true,
                IgnoreUserSettingsObjects = true,
                IgnoreLoginSids = true,
                IgnoreFileAndLogFilePath = true,
                IgnoreFilegroupPlacement = true,
                IgnoreFileSize = true,
                IgnoreExtendedProperties = true,
                ScriptRefreshModule = false,
            }
        };

        SchemaComparisonResult result = comparer.Compare(ct);

        if (!result.IsValid)
        {
            string[] errors = [.. result.GetErrors().Select(e => e.Message)];

            foreach (string error in errors)
                logger.LogError("Schema compare error for {Database}: {Error}", database.DatabaseName, error);

            string detail = errors.Length > 0 ? string.Join(Environment.NewLine, errors) : "no detail was reported";

            throw new InvalidOperationException($"Comparing {dacpacPath} against {database.DatabaseName} failed:{Environment.NewLine}{detail}");
        }

        List<SchemaDriftItem> items = [.. result.Differences.Select(DacFxUtils.ToDriftItem).OfType<SchemaDriftItem>()];

        logger.LogDebug("Found {Count} drifted object(s) in {Database}", items.Count, database.DatabaseName);

        return new SchemaDriftReport(database.DatabaseName, items);
    }

    public SchemaComparisonResult CompareTables(string currentDacpac, string targetDacpac, bool blockOnDataLoss = true, CancellationToken ct = default)
    {
        return CompareSchema(currentDacpac, targetDacpac, SchemaObjectTypes.Programmable, blockOnDataLoss, ct);
    }

    public SchemaComparisonResult CompareProgrammables(string currentDacpac, string targetDacpac, CancellationToken ct = default)
    {
        return CompareSchema(currentDacpac, targetDacpac, SchemaObjectTypes.Structure, ct: ct);
    }

    public SchemaComparisonResult CompareAll(string currentDacpac, string targetDacpac, CancellationToken ct = default)
    {
        return CompareSchema(currentDacpac, targetDacpac, [], ct: ct);
    }
}
