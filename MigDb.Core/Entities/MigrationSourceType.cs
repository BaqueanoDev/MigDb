namespace MigDb.Core.Entities;

/// <summary>
/// Migration directory source
/// 
/// Used to keep track of what scripts and migrations 
/// are applied and how they should be treated
/// 
/// Common: Shared between all projects
/// Project: Project specific folder
/// External: Directory outside migration root
/// </summary>
public enum MigrationSourceType
{
    Common,
    Project,
    External
}
