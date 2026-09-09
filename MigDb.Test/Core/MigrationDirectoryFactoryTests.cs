using MigDb.Core.Entities;
using MigDb.Core.Migration;
using MigDb.Core.Options;
using MigDb.Test.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;

namespace MigDb.Test.Core;

public class MigrationDirectoryFactoryTests
{
    private static MigrationDirectoryFactory CreateFactory(string rootPath) =>
        new(NullLogger<MigrationDirectoryFactory>.Instance,
            new MigrationPathResolver(NullLogger<MigrationPathResolver>.Instance, new MigrationOptions { SchemaProjectPath = rootPath }));

    [Fact]
    public void Get_WhenDirectoryExists_InfoExistsTrue()
    {
        using TestDirectory temp = new();
        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);

        temp.CreateSubDirectory(Path.Join("Migrations", "Common", "Test"));

        Assert.True(factory.GetCommon("Test").Info.Exists);
    }

    [Fact]
    public void Get_WhenDirectoryMissing_InfoExistsFalse()
    {
        using TestDirectory temp = new();
        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);

        Assert.False(factory.GetCommon("Test").Info.Exists);
    }

    [Fact]
    public void GetMigrationDirectories_Common_ReturnOrdered()
    {
        using TestDirectory temp = new();
        temp.CreateSubDirectory(Path.Join("Migrations", "Common", "b_second"));
        temp.CreateSubDirectory(Path.Join("Migrations", "Common", "a_first"));

        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);
        List<MigrationDirectory> result = factory.GetMigrationDirectories(new MigrationSource.Common());

        Assert.NotEmpty(result);
        Assert.Equal("a_first", result[0].Name);
        Assert.Equal("b_second", result[1].Name);
    }

    [Fact]
    public void GetMigrationDirectories_Common_NoSubdirectories_ReturnEmpty()
    {
        using TestDirectory temp = new();
        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);

        Assert.Empty(factory.GetMigrationDirectories(new MigrationSource.Common()));
    }

    [Fact]
    public void GetMigrationDirectories_Project_ReturnOrdered()
    {
        using TestDirectory temp = new();
        temp.CreateSubDirectory(Path.Join("Migrations", "Projects", "Sample", "20260603_000000_test"));
        temp.CreateSubDirectory(Path.Join("Migrations", "Projects", "Sample", "20260602_000000_test"));

        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);
        List<MigrationDirectory> result = factory.GetMigrationDirectories(new MigrationSource.Project("Sample"));

        Assert.NotEmpty(result);
        Assert.Equal("20260602_000000_test", result[0].Name);
        Assert.Equal("20260603_000000_test", result[1].Name);
    }

    [Fact]
    public void GetMigrationDirectories_Project_NoSubdirectories_ReturnEmpty()
    {
        using TestDirectory temp = new();
        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);

        Assert.Empty(factory.GetMigrationDirectories(new MigrationSource.Project("Sample")));
    }

    [Fact]
    public void GetMigrationDirectories_Project_EmptyProjectName_Throws()
    {
        using TestDirectory temp = new();
        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);

        Assert.ThrowsAny<ArgumentException>(() => factory.GetMigrationDirectories(new MigrationSource.Project("")));
    }

    [Fact]
    public void CreateMigrationDirectory_NameFormat_MatchFormat()
    {
        using TestDirectory temp = new();
        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);

        MigrationDirectory migration = factory.CreateMigrationDirectory(new MigrationSource.Common(), "Sample");

        Assert.True(Directory.Exists(migration.FullName));
        Assert.EndsWith("_Sample", migration.Name);
        Assert.Equal(MigrationSourceType.Common, migration.SourceType);
    }

    [Fact]
    public void CreateMigrationDirectory_AlreadyExists_Throws()
    {
        using TestDirectory temp = new();
        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);

        MigrationDirectory migration = factory.CreateMigrationDirectory(new MigrationSource.Common(), "Sample");

        Assert.True(Directory.Exists(migration.FullName));

        Assert.Throws<InvalidOperationException>(() => factory.CreateMigrationDirectory(new MigrationSource.Common(), "Sample"));
    }

    [Fact]
    public void CreateMigrationDirectory_NameContainsDirectory_Throws()
    {
        using TestDirectory temp = new();
        MigrationDirectoryFactory factory = CreateFactory(temp.Info.FullName);

        string name = $"test{Path.DirectorySeparatorChar}Sample";

        Assert.Throws<ArgumentException>(() => factory.CreateMigrationDirectory(new MigrationSource.Common(), name));
    }

    [Fact]
    public void CreateMigrationDirectoryAt_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => MigrationDirectoryFactory.CreateMigrationDirectoryAt("", "aa"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateMigrationDirectoryAt_NoName_NamesDirectoryWithTimestampOnly(string? name)
    {
        using TestDirectory temp = new();

        DirectoryInfo info = MigrationDirectoryFactory.CreateMigrationDirectoryAt(temp.Info.FullName, name);

        Assert.True(info.Exists);

        // no trailing separator: the whole name is the timestamp
        Assert.True(DateTime.TryParseExact(
            info.Name,
            MigrationDirectoryFactory.TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _));
    }
}
