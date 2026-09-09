using MigDb.Core.Utils;

namespace MigDb.Test.Core;

public class GitUtilsTests
{
    private const string LsTreeOutput =
        "040000 tree 8f2a1c9b4e5d6a7f8091a2b3c4d5e6f708192a3b\t20260101_120000\n" +
        "100644 blob e69de29bb2d1d6434b8b29ae775ad8c2e48c5391\tREADME.md\n" +
        "040000 tree 1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d\t20260827_225044\n";

    [Fact]
    public void ParseTreeDirectories_MixedEntries_ReturnsTreesInOrder()
    {
        Assert.Equal(["20260101_120000", "20260827_225044"], GitUtils.ParseTreeDirectories(LsTreeOutput));
    }

    [Fact]
    public void ParseTreeDirectories_BlobsOnly_ReturnsEmpty()
    {
        string output = "100644 blob e69de29bb2d1d6434b8b29ae775ad8c2e48c5391\tREADME.md\n";

        Assert.Empty(GitUtils.ParseTreeDirectories(output));
    }

    [Fact]
    public void ParseTreeDirectories_NameWithSpaces_KeptWhole()
    {
        string output = "040000 tree 8f2a1c9b4e5d6a7f8091a2b3c4d5e6f708192a3b\tsome migration\n";

        Assert.Equal(["some migration"], GitUtils.ParseTreeDirectories(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("not tree output")]
    public void ParseTreeDirectories_NothingUsable_ReturnsEmpty(string output)
    {
        Assert.Empty(GitUtils.ParseTreeDirectories(output));
    }

    [Fact]
    public void ParseTreeDirectories_PathspecEntries_NamedFromRepoRoot()
    {
        // a pathspec listing names entries by their full path, ListDirectoriesAtCommitAsync trims them
        string output =
            "040000 tree 8f2a1c9b4e5d6a7f8091a2b3c4d5e6f708192a3b\tMigrations/Common/20260101_120000\n" +
            "100644 blob e69de29bb2d1d6434b8b29ae775ad8c2e48c5391\tMigrations/Common/README.md\n";

        Assert.Equal(["Migrations/Common/20260101_120000"], GitUtils.ParseTreeDirectories(output));
    }

    [Fact]
    public async Task ListDirectoriesAtCommitAsync_BlankArguments_Throws()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => GitUtils.ListDirectoriesAtCommitAsync("", "HEAD", TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => GitUtils.ListDirectoriesAtCommitAsync(Path.GetTempPath(), "  ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListDirectoriesAtCommitAsync_ExistingPath_ReturnsBareNames()
    {
        string path = Path.Combine(RepositoryRoot(), "MigDb.Core", "Migration");

        IReadOnlyList<string> directories = await GitUtils.ListDirectoriesAtCommitAsync(path, "HEAD", TestContext.Current.CancellationToken);

        Assert.Contains("Generate", directories);
        Assert.Contains("Run", directories);
        Assert.DoesNotContain(directories, d => d.Contains('/'));
    }

    [Fact]
    public async Task ListDirectoriesAtCommitAsync_PathMissingAtCommit_ReturnsEmpty()
    {
        string path = Path.Combine(RepositoryRoot(), "no-such-directory-at-head");

        Assert.Empty(await GitUtils.ListDirectoriesAtCommitAsync(path, "HEAD", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The dangerous case: an empty list means "not in that tree", so a git failure has to throw
    /// rather than read as "everything under this path was added since"
    /// </summary>
    [Fact]
    public async Task ListDirectoriesAtCommitAsync_UnknownCommit_Throws()
    {
        string path = RepositoryRoot();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => GitUtils.ListDirectoriesAtCommitAsync(path, "not-a-real-commit-ish", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListDirectoriesAtCommitAsync_PathOutsideRepository_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => GitUtils.ListDirectoriesAtCommitAsync(Path.GetTempPath(), "HEAD", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Walks up from the test binaries to the repository these tests are built out of
    /// </summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory.FullName;
    }
}
