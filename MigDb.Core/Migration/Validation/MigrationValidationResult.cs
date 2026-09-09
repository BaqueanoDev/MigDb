namespace MigDb.Core.Migration.Validation;

public sealed record MigrationValidationResult(IReadOnlyList<MigrationValidationError> Errors, string Subject = "")
{
    public bool IsValid => Errors.Count == 0 && Children.All(x => x.IsValid);

    public IReadOnlyList<MigrationValidationResult> Children { get; init; } = [];
    public IEnumerable<MigrationValidationError> AllErrors => Errors.Concat(Children.SelectMany(x => x.AllErrors));

    public static MigrationValidationResult Success()
    {
        return new MigrationValidationResult([]);
    }
}
