namespace MigDb.Core.Migration.Plan;

/// <summary>
/// Links a directory to a list of possible plan files
/// </summary>
/// <param name="Directory">The migration directory the plan applies to</param>
/// <param name="Files">The planned files</param>
public record class MigrationPlan(MigrationDirectory Directory, List<MigrationPlanFile> Files);
