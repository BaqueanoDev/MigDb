using MigDb.Core.Migration.Plan;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Schema;
using MigDb.Core.Utils.ScriptDom;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MigDb.Core.Migration;

/// <summary>
/// Used to create and work with migration plans
/// </summary>
/// <param name="logger">Logger to use</param>
/// <param name="validator">Validator to use when</param>
public sealed partial class MigrationPlanner(ILogger<MigrationPlanner> logger, MigrationValidator validator)
{
    public const string StagedExtension = ".migdbpart";

    /// <summary>
    /// Regex to try capture files already prefixed with numbers
    /// tries to match 01_filename.sql, 02_filename.sql, etc
    /// </summary>
    [GeneratedRegex(@"^(\d+)_(?:\d+_)*(.+)$")]
    private static partial Regex NumberRgx();

    /// <summary>
    /// Validate a directory and builds a migration plan
    /// Files with a numeric prefix keep their own number so they are not renumbered
    /// unnumbered files are sorted after and are assigned numbers continuing from the highest one already used
    /// </summary>
    /// <param name="directory">The migration directory to plan</param>
    /// <returns>The ordered migration plan</returns>
    /// <exception cref="MigrationValidationException">
    /// Thrown when the directory or its files fail validation
    /// </exception>
    public async Task<MigrationPlan> CreateMigrationPlanAsync(MigrationDirectory directory, bool splitBatches = true, CancellationToken ct = default)
    {
        IReadOnlyList<FileInfo> sqlFiles = directory.GetDataFiles();

        List<MigrationValidationError> issues = [.. validator.ValidateDirectory(directory)];
        Dictionary<string, IReadOnlyList<string>> batches = [];

        foreach (FileInfo f in sqlFiles)
        {
            logger.LogInformation("Found file: {File}", f.Name);

            string content = await File.ReadAllTextAsync(f.FullName, ct);

            if (!splitBatches || !ScriptDomUtils.ContainsBatchToken(content))
            {
                MigrationValidationResult fileResult = await validator.ValidateFileAsync(f, ct);
                issues.AddRange(fileResult.Errors);
                continue;
            }

            IReadOnlyList<string> fileBatches = DacFxUtils.SplitScriptByBatch(content);

            logger.LogInformation("Found {Count} batch(es) in {File}", fileBatches.Count, f.Name);

            issues.AddRange(validator.ValidateBatches(f, fileBatches).AllErrors);

            batches[f.Name] = fileBatches;
        }

        if (issues.Count != 0)
        {
            foreach (MigrationValidationError i in issues)
                logger.LogError("Validation error: {Error}", i);

            throw new MigrationValidationException(issues);
        }

        logger.LogInformation("Sorting files");

        List<MigrationPlanFile> numbered = new(sqlFiles.Count);
        List<FileInfo> unnumbered = new(sqlFiles.Count);

        foreach (FileInfo f in sqlFiles)
        {
            string name = Path.GetFileNameWithoutExtension(f.Name);
            Match match = NumberRgx().Match(name);

            // keep the number the file already carries so it is not renamed
            if (match.Success && int.TryParse(match.Groups[1].ValueSpan, out int order))
            {
                logger.LogInformation("Found pre-sorted file: {File}", f.Name);

                numbered.AddRange(CreatePlanFiles(f, order, match.Groups[2].Value, batches));
            }
            else
            {
                logger.LogInformation("Found unsorted file: {File}", f.Name);

                unnumbered.Add(f);
            }
        }

        numbered = [.. numbered
            .OrderBy(f => f.Order)
            .ThenBy(f => f.GeneratedName, StringComparer.Ordinal)];

        List<MigrationPlanFile> planFiles = new(sqlFiles.Count);

        planFiles.AddRange(numbered);

        // unnumbered files carry on from the highest number already in use so they never collide
        int next = numbered.Count == 0 ? 1 : numbered.Max(f => f.Order) + 1;

        foreach (FileInfo f in unnumbered)
        {
            planFiles.AddRange(CreatePlanFiles(f, next, Path.GetFileNameWithoutExtension(f.Name), batches));
            next++;
        }

        IReadOnlyList<MigrationValidationError> planIssues = validator.ValidatePlannedNames([.. planFiles.Select(f => f.GeneratedName)]);

        if (planIssues.Count != 0)
        {
            foreach (MigrationValidationError i in planIssues)
                logger.LogError("Validation error: {Error}", i);

            throw new MigrationValidationException(planIssues);
        }

        MigrationPlan migrationPlan = new(directory, planFiles);

        return migrationPlan;
    }

    private static IEnumerable<MigrationPlanFile> CreatePlanFiles(FileInfo file, int order, string name, Dictionary<string, IReadOnlyList<string>> batches)
    {
        if (!batches.TryGetValue(file.Name, out IReadOnlyList<string>? fileBatches))
        {
            yield return new MigrationPlanFile.Copy(file, order, name);
            yield break;
        }

        if (fileBatches.Count == 1)
        {
            yield return new MigrationPlanFile.Batch(file, order, name, fileBatches[0]);
            yield break;
        }

        for (int i = 0; i < fileBatches.Count; i++)
            yield return new MigrationPlanFile.Batch(file, order, $"{name}_{i + 1:D2}", fileBatches[i]);
    }

    /// <summary>
    /// Attempts to apply a plan.
    /// Copies are staged first; originals are only deleted once every copy succeeds.
    /// </summary>
    /// <param name="plan">The plan to apply</param>
    /// <param name="dryRun">Allows simulating changes without actually applying</param>
    /// <exception cref="IOException">
    /// Propagated when a file copy fails
    /// </exception>
    /// <exception cref="AggregateException">
    /// Thrown when one or more original files could not be deleted after all copies succeeded;
    /// staged copies remain on disk so no data is lost
    /// </exception>
    public void ApplyMigrationPlan(MigrationPlan plan, bool dryRun = false)
    {
        logger.LogInformation("Applying migration plan");

        List<MigrationPlanFile> pending = [.. plan.Files.Where(f => f.NeedsWrite)];

        if (pending.Count == 0)
        {
            logger.LogInformation("Nothing to do, every file is already named and split");
            return;
        }

        List<string> staged = new(pending.Count);

        try
        {
            foreach (MigrationPlanFile f in pending)
            {
                logger.LogInformation("Moving file {Src} --> {Dst}", f.File.Name, f.GeneratedName);

                if (dryRun)
                    continue;

                string stagedPath = Path.Join(plan.Directory.FullName, f.GeneratedName + StagedExtension);

                if (f is MigrationPlanFile.Batch batch)
                    File.WriteAllText(stagedPath, batch.Content);
                else
                    File.Copy(f.File.FullName, stagedPath);

                staged.Add(stagedPath);
            }
        }
        catch (Exception)
        {
            foreach (string s in staged)
                File.Delete(s);

            throw;
        }

        if (dryRun)
            return;

        List<Exception> errors = [];

        foreach (FileInfo source in pending.Select(f => f.File).DistinctBy(f => f.FullName))
        {
            try
            {
                source.Delete();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete original file {File} after staging", source.Name);

                errors.Add(ex);
            }
        }

        if (errors.Count == 0)
        {
            foreach (string s in staged)
            {
                try
                {
                    File.Move(s, s[..^StagedExtension.Length]);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to move staged file {File} into place", Path.GetFileName(s));

                    errors.Add(ex);
                }
            }
        }

        if (errors.Count != 0)
            throw new AggregateException($"Plan could not be completed; staged files remain on disk as *{StagedExtension}", errors);
    }
}
