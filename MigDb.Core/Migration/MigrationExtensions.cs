using MigDb.Core.Migration.Validation;

namespace MigDb.Core.Migration;

public static class MigrationExtensions
{
    public static string Describe(this MigrationValidationResult result)
    {
        // this result's own errors only, children are appended below
        List<string> parts = [.. result.Errors.Distinct().Select(Describe)];

        parts.AddRange(result.Children.Select(Describe));

        string body = string.Join(", ", parts);

        if (string.IsNullOrWhiteSpace(result.Subject))
            return body;

        return $"{result.Subject}: {body}";
    }

    public static string Describe(this MigrationValidationError error)
    {
        return error switch
        {
            MigrationValidationError.HasSubDirectories => "Sub-directories",
            MigrationValidationError.Empty => "No files",
            MigrationValidationError.TooManyFiles => "Too many files",
            MigrationValidationError.NonSqlFiles => "Non .sql files",
            MigrationValidationError.ManifestMissing => "Manifest missing",
            MigrationValidationError.CorruptedManifest => "Manifest invalid",
            MigrationValidationError.FileNotInManifest => "File not in manifest",
            MigrationValidationError.FileInManifestMissing => "Manifest file missing from directory",
            MigrationValidationError.DirectoryHashMismatch => "Content changed since prepare",
            MigrationValidationError.FileHashMismatch => "File hash mismatch",
            MigrationValidationError.ContainsBatchToken => "Contains batch token",
            MigrationValidationError.GeneratedNameCollision => "More than one file plans to become the same name",
            MigrationValidationError.SQLParseError => "SQL parse error",
            MigrationValidationError.EmptyFile => "Empty file",
            _ => error.ToString(),
        };
    }
}
