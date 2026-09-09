using MigDb.Core.Migration;
using MigDb.Core.Migration.Manifest;
using MigDb.Core.Migration.Validation;
using MigDb.Test.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigDb.Test.Core;

public class MigrationValidatorTests
{
    private const string TestSql = "SELECT * FROM migdb.MigrationRun;";

    private static MigrationHasher CreateHasher() => new(NullLogger<MigrationHasher>.Instance);

    private static MigrationManifestStore CreateStore() => new(CreateHasher(), NullLogger<MigrationManifestStore>.Instance);

    private static MigrationValidator CreateValidator() => new(CreateHasher(), CreateStore(), NullLogger<MigrationValidator>.Instance);

    private readonly MigrationHasher hasher = CreateHasher();

    private readonly MigrationManifestStore store = CreateStore();

    private static MigrationDirectory CreateMigrationDirectory(TestDirectory tmp) => new(tmp.Info, new MigrationSource.Common());

    [Fact]
    public async Task Verify_PreparedManifest_True()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);
        tmp.WriteFile("02_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);
        MigrationManifest m = await store.CreateManifestAsync(dir, ct);

        store.WriteManifest(dir, m);

        Assert.True((await validator.ValidateDirectoryAsync(dir, ct)).IsValid);
    }

    [Fact]
    public async Task Verify_MissingManifest_False()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);
        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        Assert.False((await validator.ValidateDirectoryAsync(dir, ct)).IsValid);
    }

    [Fact]
    public async Task Verify_EditedFile_False()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        tmp.WriteFile("01_test.sql", "SELECT 2;");

        Assert.False((await validator.ValidateDirectoryAsync(dir, ct)).IsValid);
    }

    [Fact]
    public async Task Verify_NewFile_False()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        tmp.WriteFile("02_test.sql", TestSql);

        Assert.False((await validator.ValidateDirectoryAsync(dir, ct)).IsValid);
    }

    [Fact]
    public async Task Verify_DeleteFile_False()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);
        string second = tmp.WriteFile("02_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        File.Delete(second);

        Assert.False((await validator.ValidateDirectoryAsync(dir, ct)).IsValid);
    }

    [Fact]
    public async Task Validate_Success()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);
        tmp.WriteFile("02_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(dir, ct);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_HasSubDirectories()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        Directory.CreateDirectory(Path.Join(tmp.Info.FullName, "sub"));

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(dir, ct);

        Assert.Contains(MigrationValidationError.HasSubDirectories, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_Empty()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(CreateMigrationDirectory(tmp), ct);

        Assert.Contains(MigrationValidationError.Empty, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_TooManyFiles()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        for (int i = 1; i <= 100; i++)
            tmp.WriteFile($"{i:000}_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(dir, ct);

        Assert.Contains(MigrationValidationError.TooManyFiles, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_NonSqlFiles()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        tmp.WriteFile("notes.txt", "not sql");

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(dir, ct);

        Assert.Contains(MigrationValidationError.NonSqlFiles, result.AllErrors);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_ValidationError_ManifestMissing()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(CreateMigrationDirectory(tmp), ct);

        Assert.Contains(MigrationValidationError.ManifestMissing, result.Errors);
        Assert.DoesNotContain(MigrationValidationError.FileNotInManifest, result.Errors);
        Assert.DoesNotContain(MigrationValidationError.DirectoryHashMismatch, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_BadManifestJSON()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);
        tmp.WriteFile(MigrationManifest.FileName, "{bad");

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(CreateMigrationDirectory(tmp), ct);

        Assert.Single(result.Errors);
        Assert.Contains(MigrationValidationError.CorruptedManifest, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_FileNotInManifest()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        tmp.WriteFile("02_test.sql", TestSql);

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(dir, ct);

        Assert.Contains(MigrationValidationError.FileNotInManifest, result.Errors);
        Assert.Contains(MigrationValidationError.DirectoryHashMismatch, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_HashMismatch()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        tmp.WriteFile("01_test.sql", "SELECT 2;");

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(dir, ct);

        Assert.Contains(MigrationValidationError.DirectoryHashMismatch, result.Errors);
        Assert.DoesNotContain(MigrationValidationError.FileNotInManifest, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_ManifestFileMissing()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);
        string second = tmp.WriteFile("02_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        File.Delete(second);

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(dir, ct);

        Assert.Contains(MigrationValidationError.FileInManifestMissing, result.Errors);
        Assert.Contains(MigrationValidationError.DirectoryHashMismatch, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_ManifestFileHashMismatch()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        store.WriteManifest(dir, await store.CreateManifestAsync(dir, ct));

        tmp.WriteFile("01_test.sql", "SELECT 2;");

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(dir, ct);

        Assert.Contains(MigrationValidationError.FileHashMismatch, result.Errors);
        Assert.Contains(MigrationValidationError.DirectoryHashMismatch, result.Errors);
    }

    [Fact]
    public async Task Validate_ValidationError_SQLParseError()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", "INVALID SQL GARBAGE %%%;");

        MigrationValidationResult result = await validator.ValidateDirectoryAsync(CreateMigrationDirectory(tmp), ct);

        // parse errors belong to the file, AllErrors rolls the children up
        Assert.Contains(MigrationValidationError.SQLParseError, result.AllErrors);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task HasDrifted_Unchanged_False()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory directory = CreateMigrationDirectory(tmp);
        byte[] appliedHash = (await hasher.HashDirectoryAsync(directory, ct)).DirectoryHash;

        Assert.False(await validator.HasDriftedAsync(directory, appliedHash, ct));
    }

    [Fact]
    public async Task HasDrifted_EditedFile_True()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory directory = CreateMigrationDirectory(tmp);
        byte[] appliedHash = (await hasher.HashDirectoryAsync(directory, ct)).DirectoryHash;

        tmp.WriteFile("01_test.sql", "SELECT 1;");

        Assert.True(await validator.HasDriftedAsync(directory, appliedHash, ct));
    }

    [Fact]
    public async Task HasDrifted_AddedFile_True()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory directory = CreateMigrationDirectory(tmp);
        byte[] appliedHash = (await hasher.HashDirectoryAsync(directory, ct)).DirectoryHash;

        tmp.WriteFile("02_extra.sql", TestSql);

        Assert.True(await validator.HasDriftedAsync(directory, appliedHash, ct));
    }

    [Fact]
    public async Task HasDrifted_WhitespaceOnlyEdit_False()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory directory = CreateMigrationDirectory(tmp);
        byte[] appliedHash = (await hasher.HashDirectoryAsync(directory, ct)).DirectoryHash;

        tmp.WriteFile("01_test.sql", $"{TestSql}   \r\n\r\n");

        Assert.False(await validator.HasDriftedAsync(directory, appliedHash, ct));
    }

    [Fact]
    public async Task HasDrifted_RePreparedAfterEdit_StillTrue()
    {
        using TestDirectory tmp = new();
        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory directory = CreateMigrationDirectory(tmp);
        byte[] appliedHash = (await hasher.HashDirectoryAsync(directory, ct)).DirectoryHash;

        tmp.WriteFile("01_test.sql", "SELECT 1;");

        MigrationManifest manifest = await store.CreateManifestAsync(directory, ct);
        store.WriteManifest(directory, manifest);

        MigrationValidationResult validation = await validator.ValidateDirectoryAsync(directory, ct);

        Assert.True(validation.IsValid);
        Assert.True(await validator.HasDriftedAsync(directory, appliedHash, ct));
    }
}
