namespace MigDb.Core.Schema;

/// <summary>
/// Used to add some sugar around commitish handling
/// Imagine having two chances to use discriminary unions in the same project :)
/// </summary>
public abstract record SchemaRevision
{
    public const string WorkingTreeToken = "working";
    public const string HeadCommitish = "HEAD";
    public const string PreviousCommit = "HEAD~1";

    public sealed record WorkingTree : SchemaRevision;
    public sealed record History(string Commitish) : SchemaRevision;

    private SchemaRevision() { }

    public static SchemaRevision Parse(string spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);

        string trimmed = spec.Trim();

        if (string.Equals(trimmed, WorkingTreeToken, StringComparison.OrdinalIgnoreCase))
            return new WorkingTree();

        return new History(trimmed);
    }

    public static string Describe(SchemaRevision revision)
    {
        return revision switch
        {
            WorkingTree => "working tree",
            History h => h.Commitish,
            _ => throw new ArgumentOutOfRangeException(nameof(revision)),
        };
    }
}
