using MigDb.Core.Entities;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration;
using MigDb.Core.Options;
using MigDb.Test.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigDb.Test.Core;

public class MigrationSelectorTests
{
    private static MigrationSelector CreateSelector(string rootPath)
    {
        MigrationOptions options = new() { SchemaProjectPath = rootPath };
        DatabaseOptions dbOptions = new();

        MigrationPathResolver pathResolver = new(NullLogger<MigrationPathResolver>.Instance, options);
        MigrationDirectoryFactory factory = new(NullLogger<MigrationDirectoryFactory>.Instance, pathResolver);

        SQLConnectionFactory sqlFactory = new(NullLogger<SQLConnectionFactory>.Instance);
        MigrationDirectoryRepository repo = new(NullLogger<MigrationDirectoryRepository>.Instance, sqlFactory, dbOptions);

        return new MigrationSelector(NullLogger<MigrationSelector>.Instance, factory, repo, options);
    }

    private static DirectoryInfo CreateCommonMigration(TestDirectory temp, string name)
    {
        return Directory.CreateDirectory(Path.Join(temp.Info.FullName, "Migrations", "Common", name));
    }

    [Fact]
    public void Resolve_BareName_ResolvesUnderConfiguredRoot()
    {
        using TestDirectory temp = new();
        DirectoryInfo expected = CreateCommonMigration(temp, "20260812_021415_Test");

        MigrationSelector selector = CreateSelector(temp.Info.FullName);

        MigrationDirectory result = selector.Resolve(new MigrationSource.Common(), "20260812_021415_Test");

        Assert.Equal(expected.FullName, result.FullName);
        Assert.Equal(MigrationSourceType.Common, result.SourceType);
    }

    [Fact]
    public void Resolve_BareName_KeepsRequestedSource()
    {
        using TestDirectory temp = new();
        Directory.CreateDirectory(Path.Join(temp.Info.FullName, "Migrations", "Projects", "Sample", "20260812_021415_Test"));

        MigrationSelector selector = CreateSelector(temp.Info.FullName);

        MigrationDirectory result = selector.Resolve(new MigrationSource.Project("Sample"), "20260812_021415_Test");

        Assert.Equal(MigrationSourceType.Project, result.SourceType);
        Assert.Equal("Sample", result.SourceName);
    }

    [Fact]
    public void Resolve_BareName_Missing_Throws()
    {
        using TestDirectory temp = new();
        MigrationSelector selector = CreateSelector(temp.Info.FullName);

        Assert.Throws<DirectoryNotFoundException>(() => selector.Resolve(new MigrationSource.Common(), "20260812_021415_Nope"));
    }

    [Fact]
    public void Resolve_BareName_NoRootConfigured_Throws()
    {
        MigrationSelector selector = CreateSelector(string.Empty);

        Assert.Throws<InvalidOperationException>(() => selector.Resolve(new MigrationSource.Common(), "20260812_021415_Test"));
    }

    [Fact]
    public void Resolve_FullPath_ResolvesAsExternal()
    {
        using TestDirectory temp = new();
        DirectoryInfo target = temp.CreateSubDirectory("20260812_021415_Outside");

        // the path is outside any configured root, so the root must not be consulted
        MigrationSelector selector = CreateSelector(string.Empty);

        MigrationDirectory result = selector.Resolve(new MigrationSource.Common(), target.FullName);

        Assert.Equal(target.FullName, result.FullName);
        Assert.Equal(MigrationSourceType.External, result.SourceType);
        Assert.Null(result.SourceName);
    }

    [Fact]
    public void Resolve_AltSeparatorPath_TreatedAsPathNotName()
    {
        using TestDirectory temp = new();
        DirectoryInfo target = temp.CreateSubDirectory("20260812_021415_Outside");

        // a forward-slashed path is still a path on Windows - it must not be
        // mistaken for a bare migration name and looked up under the root
        string altPath = target.FullName.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        MigrationSelector selector = CreateSelector(string.Empty);

        MigrationDirectory result = selector.Resolve(new MigrationSource.Common(), altPath);

        Assert.Equal(MigrationSourceType.External, result.SourceType);
        Assert.True(result.Info.Exists);
    }

    [Fact]
    public void Resolve_FullPath_Missing_Throws()
    {
        using TestDirectory temp = new();
        string missing = Path.Join(temp.Info.FullName, "20260812_021415_Nope");

        MigrationSelector selector = CreateSelector(string.Empty);

        Assert.Throws<DirectoryNotFoundException>(() => selector.Resolve(new MigrationSource.Common(), missing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resolve_EmptyNameOrPath_Throws(string? nameOrPath)
    {
        using TestDirectory temp = new();
        MigrationSelector selector = CreateSelector(temp.Info.FullName);

        Assert.ThrowsAny<ArgumentException>(() => selector.Resolve(new MigrationSource.Common(), nameOrPath!));
    }
}
