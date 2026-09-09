using MigDb.Core.Migration;
using MigDb.Core.Migration.Manifest;
using MigDb.Test.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigDb.Test.Core;

public class MigrationHasherTests
{
    private const string TestSql = "SELECT * FROM migdb.MigrationRun;";

    private static MigrationHasher CreateHasher() => new(NullLogger<MigrationHasher>.Instance);

    private readonly MigrationHasher hasher = CreateHasher();

    private static MigrationDirectory CreateMigrationDirectory(TestDirectory tmp) => new(tmp.Info, new MigrationSource.Common());

    [Fact]
    public async Task HashFile_Hashing_Consistent()
    {
        using TestDirectory tmp = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        FileInfo file = new(tmp.WriteFile("a.sql", TestSql));

        byte[] aHash = (await hasher.HashFileAsync(file, ct)).Hash;
        byte[] bHash = (await hasher.HashFileAsync(file, ct)).Hash;

        Assert.Equal(aHash, bHash);
    }

    [Fact]
    public async Task HashFile_Hashing_DifferentContent_DifferentHash()
    {
        using TestDirectory tmp = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        FileInfo aFile = new(tmp.WriteFile("a.sql", TestSql));
        FileInfo bFile = new(tmp.WriteFile("b.sql", "Test123"));

        Assert.NotEqual((await hasher.HashFileAsync(aFile, ct)).Hash, (await hasher.HashFileAsync(bFile, ct)).Hash);
    }

    [Fact]
    public async Task HashFile_NormalisedLineEnding_Consistent()
    {
        using TestDirectory tmp1 = new();
        using TestDirectory tmp2 = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        FileInfo crlf = new(tmp1.WriteFile("a.sql", "SELECT 1;\r\nSELECT 2;\r\n"));
        FileInfo lf = new(tmp2.WriteFile("a.sql", "SELECT 1;\nSELECT 2;\n"));

        Assert.Equal((await hasher.HashFileAsync(crlf, ct)).Hash, (await hasher.HashFileAsync(lf, ct)).Hash);
    }

    [Fact]
    public async Task HashFile_NormalisedWhiteSpace_Consistent()
    {
        using TestDirectory tmp1 = new();
        using TestDirectory tmp2 = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        FileInfo trailing = new(tmp1.WriteFile("a.sql", "SELECT 1;   \nSELECT 2;"));
        FileInfo clean = new(tmp2.WriteFile("a.sql", "SELECT 1;\nSELECT 2;\n"));

        Assert.Equal((await hasher.HashFileAsync(trailing, ct)).Hash, (await hasher.HashFileAsync(clean, ct)).Hash);
    }

    [Fact]
    public async Task HashDirectory_ManifestFile_Ignored()
    {
        using TestDirectory tmp = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectoryHashResult before = await hasher.HashDirectoryAsync(CreateMigrationDirectory(tmp), ct);

        tmp.WriteFile(MigrationManifest.FileName, "{}");

        MigrationDirectoryHashResult after = await hasher.HashDirectoryAsync(CreateMigrationDirectory(tmp), ct);

        Assert.Equal(before.DirectoryHash, after.DirectoryHash);
    }

    [Fact]
    public async Task HashDirectory_RenamedFile_DifferentHash()
    {
        using TestDirectory tmp = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        string path = tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectoryHashResult before = await hasher.HashDirectoryAsync(CreateMigrationDirectory(tmp), ct);

        File.Move(path, Path.Join(tmp.Info.FullName, "02_test.sql"));

        MigrationDirectoryHashResult after = await hasher.HashDirectoryAsync(CreateMigrationDirectory(tmp), ct);

        Assert.NotEqual(before.DirectoryHash, after.DirectoryHash);
    }

    [Fact]
    public async Task HashDirectory_RenamedDirectory_DifferentHash()
    {
        using TestDirectory tmp = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        string path = tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);

        MigrationDirectoryHashResult before = await hasher.HashDirectoryAsync(dir, ct);

        string newPath = Path.Combine(Path.GetTempPath(), $"migdb_testMoved_{Guid.NewGuid():N}");

        dir.Info.MoveTo(newPath);

        MigrationDirectoryHashResult after = await hasher.HashDirectoryAsync(dir, ct);

        Assert.NotEqual(before.DirectoryHash, after.DirectoryHash);
    }

}
