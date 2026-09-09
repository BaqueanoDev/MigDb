using MigDb.Core.Migration.Generate;
using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;
using MigDb.Core.Utils;
using MigDb.Core.Utils.ScriptDom;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Dac.Compare;

namespace MigDb.Core.Migration;

public sealed class MigrationGenerator(ILogger<MigrationGenerator> logger, SchemaBuilder builder, SchemaComparer comparer)
{
    public async Task<IReadOnlyList<MigrationScript>> GenerateAsync(SchemaSource source, SchemaRevision sourceRevision, SchemaRevision targetRevision, SchemaDeployScope scope = SchemaDeployScope.All, bool blockOnDataLoss = true, CancellationToken ct = default)
    {
        bool includeTables = scope is SchemaDeployScope.All or SchemaDeployScope.Schema;
        bool includeProgrammables = scope is SchemaDeployScope.All or SchemaDeployScope.Programmables;

        DirectoryInfo? sourceBuildDirectory = null;
        DirectoryInfo? targetBuildDirectory = null;

        // this is where the magic happens
        // we create two working directories (source & target)
        // build the projects and dacpacs so we can call the comparer for schema tables
        // 
        // for programmables we use our hasher and check what
        // files are missing, hashes changed or new and return a modified script to apply it
        try
        {
            sourceBuildDirectory = Directory.CreateTempSubdirectory("migdb-build");
            targetBuildDirectory = Directory.CreateTempSubdirectory("migdb-build");

            logger.LogDebug("Generating {Scope} scripts, source {Source} & target {Target}", scope, SchemaRevision.Describe(sourceRevision), SchemaRevision.Describe(targetRevision));

            string[] roots = await Task.WhenAll(
                builder.ResolveSourceRootAsync(source, sourceRevision, sourceBuildDirectory.FullName, ct: ct),
                builder.ResolveSourceRootAsync(source, targetRevision, targetBuildDirectory.FullName, ct: ct));

            string sourceRoot = roots[0];
            string targetRoot = roots[1];

            IReadOnlyList<MigrationScript> tableScripts = [];

            if (includeTables)
            {
                string[] dacpacs = await Task.WhenAll(
                    builder.BuildAtPathAsync(sourceRoot, sourceBuildDirectory.FullName, ct: ct),
                    builder.BuildAtPathAsync(targetRoot, targetBuildDirectory.FullName, ct: ct));

                tableScripts = GenerateTableScripts(dacpacs[0], dacpacs[1], blockOnDataLoss, ct);
            }

            List<MigrationScript> scripts = [.. tableScripts];

            if (includeProgrammables)
            {
                SchemaProgrammableComparisonResult programmables = await comparer.CompareProgrammableFilesAsync(sourceRoot, targetRoot, source, ct);

                // the object is already in the target database, a bare CREATE from the file would fail
                scripts.AddRange(programmables.Added.Concat(programmables.Changed).Select(p => new MigrationScript(new SchemaObject(p.Schema, p.ObjectName, p.Type), ScriptDomUtils.ToCreateOrAlter(p.Script), [], 0)));
                scripts.AddRange(programmables.Removed.Select(p => new MigrationScript(new SchemaObject(p.Schema, p.ObjectName, p.Type), SchemaUtils.ToDropScript(p.Type, p.Schema, p.ObjectName), [], 0)));
            }

            logger.LogDebug("Generated {Count} script(s) for {Source} -> {Target}", scripts.Count, SchemaRevision.Describe(sourceRevision), SchemaRevision.Describe(targetRevision));

            return Renumber(scripts);
        }
        finally
        {
            if (sourceBuildDirectory is not null)
            {
                try
                {
                    sourceBuildDirectory.Delete(recursive: true);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Could not delete working directory {Dir}, will need to manually clean up (find in env tmp)", sourceBuildDirectory.FullName);
                }
            }

            if (targetBuildDirectory is not null)
            {
                try
                {
                    targetBuildDirectory.Delete(recursive: true);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Could not delete working directory {Dir}, will need to manually clean up (find in env tmp)", targetBuildDirectory.FullName);
                }
            }
        }
    }

    public IReadOnlyList<MigrationScript> GenerateTableScripts(string sourceDacpac, string targetDacpac, bool blockOnDataLoss = true, CancellationToken ct = default)
    {
        SchemaComparisonResult compareResult = comparer.CompareTables(sourceDacpac, targetDacpac, blockOnDataLoss, ct);

        if (!compareResult.IsValid)
            throw new InvalidOperationException("Schema comparer returned an invalid result.");

        if (compareResult.IsEqual)
        {
            logger.LogDebug("No table differences");
            return [];
        }

        SchemaCompareScriptGenerationResult deltaScript = compareResult.GenerateScript("MigDb", ct);

        if (!deltaScript.Success)
            throw new InvalidOperationException("Schema delta script generation failed.");

        IReadOnlyList<SchemaObjectScript> objectScripts = DacFxUtils.SplitScriptsByObject(DacFxUtils.DenoiseScript(deltaScript.Script));

        return Renumber(objectScripts.Select(s => new MigrationScript(s.Object, s.Script, s.Warnings, 0)));
    }

    private static IReadOnlyList<MigrationScript> Renumber(IEnumerable<MigrationScript> scripts)
    {
        return [.. scripts.Select((script, index) => script with { Sequence = index + 1 })];
    }
}
