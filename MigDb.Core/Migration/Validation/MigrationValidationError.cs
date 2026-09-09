namespace MigDb.Core.Migration.Validation;

/// <summary>
/// Represents a set of possible detectable errors
/// when preparing, checkign or validating migrations
/// </summary>
public enum MigrationValidationError
{
    Empty,
    HasSubDirectories,
    TooManyFiles,
    NonSqlFiles,
    ManifestMissing,
    CorruptedManifest,
    FileNotInManifest,
    FileInManifestMissing,
    DirectoryHashMismatch,
    FileHashMismatch,
    ContainsBatchToken,
    GeneratedNameCollision,
    SQLParseError,
    EmptyFile
}