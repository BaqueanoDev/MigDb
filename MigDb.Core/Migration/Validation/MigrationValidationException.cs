namespace MigDb.Core.Migration.Validation;

public sealed class MigrationValidationException(IReadOnlyList<MigrationValidationError> errors)
    : Exception($"Migration directory failed validation: {string.Join(", ", errors)}")
{
    public IReadOnlyList<MigrationValidationError> Errors { get; } = errors;
}
