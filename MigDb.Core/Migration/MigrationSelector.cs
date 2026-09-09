using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Options;
using Microsoft.Extensions.Logging;
using System.Data;

namespace MigDb.Core.Migration;

/// <summary>
/// Another one
/// </summary>
/// <param name="logger">Logger to use</param>
/// <param name="validator">Validator to use when</param>
public sealed partial class MigrationSelector(ILogger<MigrationSelector> logger, MigrationDirectoryFactory directoryFactory, MigrationDirectoryRepository directoryRepository, MigrationOptions options)
{
    /// <summary>
    /// Get a migration directory
    /// </summary>
    /// <param name="source">Source to pull from</param>
    /// <param name="name">Name of migration</param>
    /// <returns></returns>
    public MigrationDirectory Get(MigrationSource source, string name)
    {
        logger.LogDebug("Selecting migration {Name} for {Source}", name, MigrationSource.ToMigrationSourceType(source));

        return directoryFactory.Get(source, name);
    }

    /// <summary>
    /// Attempts to resolve path from a source
    /// </summary>
    /// <param name="source"></param>
    /// <param name="nameOrPath"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="DirectoryNotFoundException"></exception>
    public MigrationDirectory Resolve(MigrationSource source, string nameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameOrPath);

        bool isPath = nameOrPath.Contains(Path.DirectorySeparatorChar) || nameOrPath.Contains(Path.AltDirectorySeparatorChar);

        MigrationDirectory result;

        if (isPath)
        {
            logger.LogDebug("Resolving {Path} as an external migration directory", nameOrPath);

            result = new MigrationDirectory(nameOrPath, new MigrationSource.External());
        }
        else
        {
            logger.LogDebug("Resolving {Name} against the project path", nameOrPath);

            if (string.IsNullOrWhiteSpace(options.SchemaProjectPath))
                throw new InvalidOperationException("No migration root is configured. Set the migration path in config, or pass a full path");

            result = Get(source, nameOrPath);
        }

        if (!result.Info.Exists)
            throw new DirectoryNotFoundException($"Migration directory does not exist: {result.FullName}");

        return result;
    }

    /// <summary>
    /// Get all pending migration directories
    /// </summary>
    /// <param name="source">Source to pull from</param>
    /// <param name="transaction">DB transaction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of all missing migration directories</returns>
    public async Task<List<MigrationDirectory>> GetPendingAsync(MigrationSource source, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        logger.LogDebug("Selecting pending migrations for {Source}", MigrationSource.ToMigrationSourceType(source));

        List<MigrationDirectory> lMigrations = directoryFactory.GetMigrationDirectories(source);

        IReadOnlyList<MigrationDirectoryEntity> dMigrations = await directoryRepository.GetAllAsync(MigrationSource.ToMigrationSourceType(source), transaction, ct);

        HashSet<string> applied = dMigrations.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<MigrationDirectory> result = [.. lMigrations.Where(x => !applied.Contains(x.Name))];

        return result;
    }

    /// <summary>
    /// Gets the tail end of pending migrations (from latest applied onwards)
    /// Intentionally skips missing or previous migrations
    /// </summary>
    /// <param name="source">Source to pull from</param>
    /// <param name="transaction">DB transaction</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>List of pending migrations after latest applied</returns>
    public async Task<List<MigrationDirectory>> GetPendingTailAsync(MigrationSource source, IDbTransaction? transaction = null, CancellationToken ct = default)
    {
        logger.LogDebug("Selecting latest pending migrations for {Source}", source);

        List<MigrationDirectory> lMigrations = directoryFactory.GetMigrationDirectories(source);

        MigrationDirectoryEntity? latest = await directoryRepository.GetLatestAsync(MigrationSource.ToMigrationSourceType(source), transaction, ct);

        if (latest is null)
            return lMigrations;

        // since directories are timestamped we can compare
        // ordinal to get tail of latest not applied
        List<MigrationDirectory> result = [.. lMigrations.Where(x => string.CompareOrdinal(x.Name, latest.Name) > 0)];

        return result;
    }

    /// <summary>
    /// Gets a list of migrations by date range
    /// 
    /// Compares time stamp prefix and returns migrations within
    /// </summary>
    /// <param name="source">Source to pull</param>
    /// <param name="from">Start date to compare</param>
    /// <param name="to">End date to compare</param>
    /// <returns>List of migrations within date range</returns>
    public List<MigrationDirectory> GetByDateRange(MigrationSource source, DateTime from, DateTime? to = null)
    {
        logger.LogDebug("Selecting migrations range from {Source} ({From} - {To})", MigrationSource.ToMigrationSourceType(source), from, to);

        string sDate = from.ToString(MigrationDirectoryFactory.TimestampFormat);
        string eDate = (to ?? DateTime.UtcNow).ToString(MigrationDirectoryFactory.TimestampFormat);

        // number of chars we will compare, we only need timestamp substring
        // this should also avoid having to cast string to date since we can
        // use compare ordinal same as getting pending
        int prefixLength = MigrationDirectoryFactory.TimestampFormat.Length;

        List<MigrationDirectory> lMigrations = directoryFactory.GetMigrationDirectories(source);

        List<MigrationDirectory> result = [.. lMigrations.Where(x =>
        {
            // TODO: Review, should malformed names e.g. < prefixLength be considered here or better to skip?
            string k = x.Name.Length >= prefixLength ? x.Name[..prefixLength] : x.Name;

            return string.CompareOrdinal(k, sDate) >= 0 && string.CompareOrdinal(k, eDate) <= 0;
        })];

        return result;
    }
}
