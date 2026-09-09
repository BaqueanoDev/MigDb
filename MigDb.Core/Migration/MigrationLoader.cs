using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;

namespace MigDb.Core.Migration;

/// <summary>
/// Used to fully load entities and supplement repos 
/// where entities with full navigation are required
/// 
/// Allows callers to decide between light weight POCO's
/// and fully navigational entities
/// </summary>
/// <param name="logger">Logger to use</param>
/// <param name="runRepo">Migration run repo</param>
/// <param name="directoryRepo">Migration directory repo</param>
/// <param name="fileRepo">Migration file repo</param>
/// <param name="programmableRepo">Migration programmable (per-run deploy) repo</param>
/// <param name="schemaProgrammableRepo">Schema programmable (current-state) repo</param>
public sealed class MigrationLoader(ILogger<MigrationLoader> logger, MigrationRunRepository runRepo, MigrationDirectoryRepository directoryRepo, MigrationFileRepository fileRepo, MigrationProgrammableRepository programmableRepo, SchemaProgrammableRepository schemaProgrammableRepo)
{

    /// <summary>
    /// Used to get a fully populated MigrationRunEntity
    /// </summary>
    /// <param name="runId">Id of run to fetch</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>If found a fully traversable MigrationRunEntity else null</returns>
    public async Task<MigrationRunEntity?> GetRunTreeAsync(long runId, CancellationToken ct = default)
    {
        logger.LogDebug("Getting run tree: {Run}", runId);

        MigrationRunEntity? run = await runRepo.GetByIdAsync(runId, ct: ct);

        if (run is null)
        {
            logger.LogDebug("Run not found");
            return null;
        }

        logger.LogDebug("Getting directories for run: {Run}", run.MigrationRunId);

        List<MigrationDirectoryEntity> directoryList = (await directoryRepo.GetByRunIdAsync(runId, ct: ct))
            .OrderBy(f => f.Sequence)
            .ToList();

        logger.LogDebug("Getting files for run: {Run}", run.MigrationRunId);

        IReadOnlyList<MigrationFileEntity> fileList = await fileRepo.GetByDirectoryIdListAsync([.. directoryList.Select(f => f.MigrationDirectoryId)], ct: ct);
        ILookup<long, MigrationFileEntity> filesByDirectory = fileList.ToLookup(f => f.MigrationDirectoryId);

        // assign files returned from batch to directory entity
        foreach (MigrationDirectoryEntity? directory in directoryList)
        {
            directory.Files = [.. filesByDirectory[directory.MigrationDirectoryId]];
            directory.MigrationRun = run;

            logger.LogDebug("Found: {Files} for {Directory}", directory.Files.Count, directory.Name);
        }

        run.Directories = directoryList;

        logger.LogDebug("Getting programmables for run: {Run}", run.MigrationRunId);

        IReadOnlyList<MigrationProgrammableEntity> programmableList = await programmableRepo.GetByRunIdAsync(runId, ct: ct);

        List<long> schemaProgrammableIds = programmableList
            .Select(p => p.SchemaProgrammableId)
            .Distinct()
            .ToList();

        IReadOnlyList<SchemaProgrammableEntity> schemaProgrammables = await schemaProgrammableRepo.GetByIdListAsync(schemaProgrammableIds, ct: ct);
        Dictionary<long, SchemaProgrammableEntity> schemaProgrammablesLookup = schemaProgrammables.ToDictionary(s => s.SchemaProgrammableId);

        foreach (MigrationProgrammableEntity programmable in programmableList)
        {
            programmable.MigrationRun = run;

            if (schemaProgrammablesLookup.TryGetValue(programmable.SchemaProgrammableId, out SchemaProgrammableEntity? schemaProgrammable))
                programmable.SchemaProgrammable = schemaProgrammable;
        }

        run.Programmables = [.. programmableList];

        logger.LogDebug("Found: {Programmables} programmable deploy(s) for run {Run}", run.Programmables.Count, run.MigrationRunId);

        return run;
    }

    /// <summary>
    /// Used to get a fully populated MigrationDirectoryEntity
    ///
    /// Parent run is fanned out to include all directories and files
    /// </summary>
    /// <param name="directoryId">Id of directory to fetch</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>If found a fully traversable MigrationDirectoryEntity else null</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the directory references a run that no longer exists, or
    /// when the resolved run does not contain the requested directory.
    /// </exception>
    public async Task<MigrationDirectoryEntity?> GetDirectoryTreeAsync(long directoryId, CancellationToken ct = default)
    {
        logger.LogDebug("Getting directory tree: {Directory}", directoryId);

        MigrationDirectoryEntity? directory = await directoryRepo.GetByIdAsync(directoryId, ct: ct);

        if (directory is null)
        {
            logger.LogDebug("Directory not found");
            return null;
        }

        MigrationRunEntity? run = await GetRunTreeAsync(directory.MigrationRunId, ct);

        if (run is null)
            throw new InvalidOperationException($"Directory {directory.MigrationDirectoryId} references missing Run {directory.MigrationRunId}");

        MigrationDirectoryEntity? match = run.Directories.FirstOrDefault(d => d.MigrationDirectoryId == directoryId);

        if (match is null)
            throw new InvalidOperationException($"Run {run.MigrationRunId} did not contain expected Directory {directoryId}");

        return match;
    }

    /// <summary>
    /// Used to get a fully populated MigrationDirectoryEntity 
    /// 
    /// Parent run is fanned out to include all directories and files
    /// </summary>
    /// <param name="fileId">Id of file to fetch</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>If found a fully traversable MigrationFileEntity else null</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the file references a directory that no longer exists, or
    /// when the resolved directory does not contain the requested file.
    /// </exception>
    public async Task<MigrationFileEntity?> GetFileTreeAsync(long fileId, CancellationToken ct = default)
    {
        logger.LogDebug("Getting file tree: {File}", fileId);

        MigrationFileEntity? file = await fileRepo.GetByIdAsync(fileId, ct: ct);

        if (file is null)
        {
            logger.LogDebug("file not found");
            return null;
        }

        MigrationDirectoryEntity? directory = await GetDirectoryTreeAsync(file.MigrationDirectoryId, ct);

        if (directory is null)
            throw new InvalidOperationException($"File {file.MigrationFileId} references missing Directory {file.MigrationDirectoryId}");

        MigrationFileEntity? match = directory.Files.FirstOrDefault(f => f.MigrationFileId == fileId);

        if (match is null)
            throw new InvalidOperationException($"Directory {directory.MigrationDirectoryId} did not contain expected File {fileId}");

        match.MigrationDirectory = directory;

        return match;
    }

    /// <summary>
    /// Used to get a MigrationDirectoryEntity with ancestors
    ///
    /// Sibling directories are not loaded
    /// </summary>
    /// <param name="directoryId">Id of directory to fetch</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>If found a MigrationDirectoryEntity with run and files populated else null</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the directory references a run that no longer exists.
    /// </exception>
    public async Task<MigrationDirectoryEntity?> GetDirectoryChainAsync(long directoryId, CancellationToken ct = default)
    {
        logger.LogDebug("Getting directory chain: {Directory}", directoryId);

        MigrationDirectoryEntity? directory = await directoryRepo.GetByIdAsync(directoryId, ct: ct);

        if (directory is null)
        {
            logger.LogDebug("Directory not found");
            return null;
        }

        logger.LogDebug("Getting run for directory: {Directory}", directory.Name);

        MigrationRunEntity? run = await runRepo.GetByIdAsync(directory.MigrationRunId, ct: ct);

        if (run is null)
            throw new InvalidOperationException($"Directory {directory.MigrationDirectoryId} references missing Run {directory.MigrationRunId}");

        directory.MigrationRun = run;

        logger.LogDebug("Getting files for directory: {Directory}", directory.Name);

        IReadOnlyList<MigrationFileEntity> files = await fileRepo.GetByDirectoryIdAsync(directoryId, ct: ct);

        directory.Files = [.. files];

        logger.LogDebug("Found: {Files} for {Directory}", directory.Files.Count, directory.Name);

        return directory;
    }

    /// <summary>
    /// Used to get a MigrationFileEntity with its ancestor
    /// 
    /// Sibling files and parent sibling directories are not loaded
    /// </summary>
    /// <param name="fileId">Id of file to fetch</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>If found a MigrationFileEntity with directory and run populated else null</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the file references a directory that no longer exists, or
    /// when that directory references a run that no longer exists.
    /// </exception>
    public async Task<MigrationFileEntity?> GetFileChainAsync(long fileId, CancellationToken ct = default)
    {
        logger.LogDebug("Getting file chain: {File}", fileId);

        MigrationFileEntity? file = await fileRepo.GetByIdAsync(fileId, ct: ct);

        if (file is null)
        {
            logger.LogDebug("file not found");
            return null;
        }

        logger.LogDebug("Getting directory for file: {File}", file.Name);

        MigrationDirectoryEntity? directory = await directoryRepo.GetByIdAsync(file.MigrationDirectoryId, ct: ct);

        if (directory is null)
            throw new InvalidOperationException($"File {file.MigrationFileId} references missing Directory {file.MigrationDirectoryId}");

        file.MigrationDirectory = directory;

        logger.LogDebug("Getting run for file: {Run}", directory.MigrationRunId);

        MigrationRunEntity? run = await runRepo.GetByIdAsync(directory.MigrationRunId, ct: ct);

        if (run is null)
            throw new InvalidOperationException($"Directory {directory.MigrationDirectoryId} references missing Run {directory.MigrationRunId}");

        directory.MigrationRun = run;

        return file;
    }
}
