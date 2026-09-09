namespace MigDb.Core.Migration.Plan;

/// <summary>
/// A single file within a migration plan
///
/// I wish name wasnt required but we require filter
/// some parts of names when generating new names
/// </summary>
/// <param name="File">The source file info</param>
/// <param name="Order">Order within the plan</param>
/// <param name="Name">Base name to use, without the order prefix or extension</param>
public abstract record MigrationPlanFile(FileInfo File, int Order, string Name)
{
    public string GeneratedName => $"{Order:D2}_{Name}{File.Extension}";

    public abstract bool NeedsWrite { get; }

    public sealed record Copy(FileInfo File, int Order, string Name) : MigrationPlanFile(File, Order, Name)
    {
        public override bool NeedsWrite => !File.Name.Equals(GeneratedName, StringComparison.OrdinalIgnoreCase);
    }

    public sealed record Batch(FileInfo File, int Order, string Name, string Content) : MigrationPlanFile(File, Order, Name)
    {
        public override bool NeedsWrite => true;
    }
}
