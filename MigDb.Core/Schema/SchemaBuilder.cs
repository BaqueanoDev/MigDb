using MigDb.Core.Utils;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace MigDb.Core.Schema;

public sealed class SchemaBuilder(ILogger<SchemaBuilder> logger, SchemaPathResolver pathResolver)
{
    public async Task<string> BuildSourceAsync(SchemaSource source, string outputDir, SchemaSystemPlatform systemPlatform = SchemaSystemPlatform.Azure, CancellationToken ct = default)
    {
        logger.LogDebug("Building dacpac {SchemaSource}", SchemaSource.ToSchemaSourceType(source));

        var path = pathResolver.ResolveSchemaSourceRoot(source);

        return await BuildAtPathAsync(path, outputDir, systemPlatform, ct);
    }

    public async Task<string> BuildAtPathAsync(string path, string outputDirectory, SchemaSystemPlatform systemPlatform = SchemaSystemPlatform.Azure, CancellationToken ct = default)
    {
        FileInfo? proj = pathResolver.FindSchemaProject(path);

        if (proj is null)
            throw new FileNotFoundException($"No .sqlproj found in {path}.");

        return await BuildSchemaAsync(proj.FullName, outputDirectory, systemPlatform, ct);
    }

    public async Task<string> ResolveSourceRootAsync(SchemaSource source, SchemaRevision revision, string workDir, string extractPath = "src", CancellationToken ct = default)
    {
        if (revision is SchemaRevision.History history)
            return await ExtractHistoryAsync(source, history.Commitish, workDir, extractPath, ct);

        return pathResolver.ResolveSchemaSourceRoot(source);
    }

    public async Task<string> ExtractHistoryAsync(SchemaSource source, string commit, string workDir, string extractPath = "src", CancellationToken ct = default)
    {
        string root = pathResolver.ResolveSchemaSourceRoot(source);

        logger.LogDebug("Extracting {Root} at {Commit} into {WorkDir}/{ExtractPath}", root, commit, workDir, extractPath);

        return await GitUtils.ExtractCommitAsync(root, commit, workDir, extractPath, ct);
    }

    public async Task<string> BuildSchemaAsync(string sqlProjectPath, string outputDirectory, SchemaSystemPlatform systemPlatform = SchemaSystemPlatform.Azure, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        logger.LogDebug("Building schema project {Project} into {OutputDir} for platform {Platform}", sqlProjectPath, outputDirectory, systemPlatform);

        string args = $"build \"{sqlProjectPath}\" -o \"{outputDirectory}\" -p:SystemPlatform=\"{systemPlatform}\"";

        ProcessStartInfo pInfo = new()
        {
            FileName = "dotnet",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process? process = Process.Start(pInfo);

        if (process is null)
            throw new InvalidOperationException($"Failed to start build for '{sqlProjectPath}'");

        // flush pipelines to avoid lockout
        Task<string> stdOutFlush = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stdErrFlush = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        string stdout = await stdOutFlush;
        string stderr = await stdErrFlush;

        if (process.ExitCode != 0)
        {
            string messages = string.Join("\n", new[] { stdout, stderr });

            throw new InvalidOperationException(
                $"Schema build failed (exit {process.ExitCode}) for '{sqlProjectPath}'.\n{messages}");
        }

        // the built project emits <ProjectName>.dacpac; referenced dacpacs (e.g. Common) also land
        // here but we return the one for the project we built
        string dacpacName = Path.GetFileNameWithoutExtension(sqlProjectPath) + ".dacpac";
        string dacpacPath = Path.Join(outputDirectory, dacpacName);

        if (!File.Exists(dacpacPath))
            throw new FileNotFoundException("Build reported success but the dacpac was not produced.", dacpacPath);

        logger.LogDebug("Built dacpac at {Dacpac}", dacpacPath);

        return dacpacPath;
    }
}
