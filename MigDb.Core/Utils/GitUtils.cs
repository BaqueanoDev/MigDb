

using System.Diagnostics;
using System.Formats.Tar;
using System.Text;

namespace MigDb.Core.Utils;

public sealed class GitUtils
{
    public static async Task<string> RunGitAsync(string workingDir, string arguments, CancellationToken ct)
    {
        ProcessStartInfo pInfo = new()
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process? process = Process.Start(pInfo);

        if (process is null)
            throw new InvalidOperationException($"Failed to start git ({arguments})");

        Task<string> stdOutFlush = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stdErrFlush = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        string stdout = await stdOutFlush;
        string stderr = await stdErrFlush;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed (exit {process.ExitCode}).\n{stderr}");

        return stdout.Trim();
    }

    public static async Task<IReadOnlyList<string>> ListDirectoriesAtCommitAsync(string path, string commit, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);

        string workingDir = Path.GetFullPath(path);

        while (!Directory.Exists(workingDir))
        {
            string? parent = Path.GetDirectoryName(workingDir);

            if (string.IsNullOrEmpty(parent))
                throw new DirectoryNotFoundException($"No existing directory above '{path}' to run git in");

            workingDir = parent;
        }

        string topLevel = await RunGitAsync(workingDir, "rev-parse --show-toplevel", ct);

        string repoPath = Path.GetFullPath(topLevel);
        string relativePath = Path.GetRelativePath(repoPath, Path.GetFullPath(path)).Replace('\\', '/').TrimEnd('/');

        if (relativePath.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidOperationException($"'{path}' sits outside the repository at '{repoPath}'");

        string output = await RunGitAsync(repoPath, $"ls-tree \"{commit}\" -- \":(literal){relativePath}/\"", ct);

        return [.. ParseTreeDirectories(output).Select(x =>
        {
            int slash = x.LastIndexOf('/');
            return slash < 0 ? x: x[(slash + 1)..];
        })];
    }

    public static IReadOnlyList<string> ParseTreeDirectories(string lsTreeOutput)
    {
        List<string> directories = [];

        foreach (string line in lsTreeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int tab = line.IndexOf('\t');

            if (tab < 0)
                continue;

            string[] fields = line[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 2 || !string.Equals(fields[1], "tree", StringComparison.Ordinal))
                continue;

            directories.Add(line[(tab + 1)..]);
        }

        return directories;
    }

    /// <summary>
    /// Archives a directory as it stood at a commit and extracts it, so a past revision can be
    /// read off disk without touching the working tree or the index
    /// </summary>
    /// <param name="path">Directory in the repo to archive</param>
    /// <param name="commit">Commit-ish to archive at</param>
    /// <param name="workDir">Directory to write the tarball and extraction into, owned by the caller</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Path to the extracted tree</returns>
    public static async Task<string> ExtractCommitAsync(string path, string commit, string workDir, string extractPath = "src", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractPath);

        // git wants a repo relative pathspec, so walk up to the top level and back down
        string topLevel = await RunGitAsync(path, "rev-parse --show-toplevel", ct);

        string repoPath = Path.GetFullPath(topLevel);
        string relativePath = Path.GetRelativePath(repoPath, path).Replace('\\', '/');

        string tarPath = Path.Join(workDir, $"{Guid.NewGuid()}.tar");

        await RunGitAsync(repoPath, $"archive --format=tar -o \"{tarPath}\" {commit}:\"{relativePath}\"", ct);

        string src = Path.Join(workDir, extractPath);

        Directory.CreateDirectory(src);

        await TarFile.ExtractToDirectoryAsync(tarPath, src, true, ct);

        return src;
    }
}
