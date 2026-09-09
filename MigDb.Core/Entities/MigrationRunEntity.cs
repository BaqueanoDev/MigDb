namespace MigDb.Core.Entities;

public class MigrationRunEntity
{
    public long MigrationRunId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime CompleteTime { get; set; }
    public int RunDuration { get; set; }
    public MigrationRunResult RunResult { get; set; }
    public string RunResultName => RunResult.ToString();
    public string RunResultMessage { get; set; } = string.Empty;
    public string AppliedBy { get; set; } = string.Empty;

    public List<MigrationDirectoryEntity> Directories { get; set; } = [];
    public List<MigrationProgrammableEntity> Programmables { get; set; } = [];
}
