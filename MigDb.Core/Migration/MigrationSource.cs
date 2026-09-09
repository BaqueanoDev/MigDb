using MigDb.Core.Entities;

namespace MigDb.Core.Migration;

// Discriminated union, reduces a some complexity in path resolver
// C# 14 will introduce Union keyword which should allow removing abstraction
public abstract record MigrationSource
{
    private MigrationSource() { }

    public sealed record Common : MigrationSource;

    public sealed record Project(string Name) : MigrationSource;

    public sealed record External : MigrationSource;

    public static MigrationSourceType ToMigrationSourceType(MigrationSource source)
    {
        return source switch
        {
            Common => MigrationSourceType.Common,
            Project => MigrationSourceType.Project,
            External => MigrationSourceType.External,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
    }

    public static string? ToSourceName(MigrationSource source)
    {
        return source switch
        {
            Project p => p.Name,
            _ => null,
        };
    }
}
