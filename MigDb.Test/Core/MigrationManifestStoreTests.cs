using MigDb.Core.Migration;
using MigDb.Core.Migration.Manifest;
using MigDb.Test.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigDb.Test.Core;

public class MigrationManifestStoreTests
{
    private const string TestSql = "SELECT * FROM migdb.MigrationRun;";

    private static MigrationHasher CreateHasher() => new(NullLogger<MigrationHasher>.Instance);

    private static MigrationManifestStore CreateStore() => new(CreateHasher(), NullLogger<MigrationManifestStore>.Instance);

    private readonly MigrationManifestStore store = CreateStore();

    private static MigrationDirectory CreateMigrationDirectory(TestDirectory tmp) => new(tmp.Info, new MigrationSource.Common());

    [Fact]
    public async Task CreateManifest_EmptyDirectory_ReturnsEmptyManifest()
    {
        using TestDirectory tmp = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        MigrationManifest manifest = await store.CreateManifestAsync(CreateMigrationDirectory(tmp), ct);

        Assert.Empty(manifest.Files);
    }

    [Fact]
    public async Task CreateManifest_MissingDirectory_Throws()
    {
        string path = Path.Join(Path.GetTempPath(), $"migdb_missing");
        DirectoryInfo missing = new(path);
        CancellationToken ct = TestContext.Current.CancellationToken;

        MigrationDirectory dir = new(missing, new MigrationSource.Common());

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => store.CreateManifestAsync(dir, ct));
    }

    [Fact]
    public async Task WriteManifest_IO_ReadWriteToDiskk()
    {
        using TestDirectory tmp = new();
        CancellationToken ct = TestContext.Current.CancellationToken;

        tmp.WriteFile("01_test.sql", TestSql);

        MigrationDirectory dir = CreateMigrationDirectory(tmp);
        MigrationManifest manifest = await store.CreateManifestAsync(dir, ct);

        store.WriteManifest(dir, manifest);

        MigrationManifest? loaded = await store.LoadManifestAsync(dir);

        Assert.NotNull(loaded);
        Assert.Equal(manifest.Files, loaded.Files);
        Assert.Equal(manifest.Hash, loaded.Hash);
        Assert.Equal(manifest.Version, loaded.Version);
    }

    [Fact]
    public async Task LoadManifest_Missing_ReturnsNull()
    {
        using TestDirectory tmp = new();

        MigrationDirectory tmpDir = CreateMigrationDirectory(tmp);
        MigrationManifest? m = await store.LoadManifestAsync(tmpDir);

        Assert.Null(m);
    }

    [Fact]
    public async Task LoadManifest_BadJSON_Throws()
    {
        using TestDirectory tmp = new();

        tmp.WriteFile(MigrationManifest.FileName, "null");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            MigrationDirectory tmpDir = CreateMigrationDirectory(tmp);
            await store.LoadManifestAsync(tmpDir);
        });
    }

}
