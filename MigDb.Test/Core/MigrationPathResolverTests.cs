using MigDb.Core.Migration;
using MigDb.Core.Options;
using MigDb.Test.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigDb.Test.Core;

public class MigrationPathResolverTests
{
    private static MigrationPathResolver CreateResolver(string rootPath) => new(NullLogger<MigrationPathResolver>.Instance, new MigrationOptions { SchemaProjectPath = rootPath });

    [Fact]
    public void GetMigrationPath_Common_ValidPath()
    {
        using TestDirectory temp = new();
        MigrationPathResolver resolver = CreateResolver(temp.Info.FullName);

        string path = Path.Join(temp.Info.FullName, "Migrations", "Common", "Test");
        string result = resolver.GetMigrationPath(new MigrationSource.Common(), "Test");

        Assert.Equal(path, result);
    }

    [Fact]
    public void GetMigrationPath_Project_ValidPath()
    {
        using TestDirectory temp = new();
        MigrationPathResolver resolver = CreateResolver(temp.Info.FullName);

        string path = Path.Join(temp.Info.FullName, "Migrations", "Projects", "Sample", "Test");
        string result = resolver.GetMigrationPath(new MigrationSource.Project("Sample"), "Test");

        Assert.Equal(path, result);
    }

    [Fact]
    public void GetMigrationPath_Project_EmptyProjectName_Throws()
    {
        using TestDirectory temp = new();
        MigrationPathResolver resolver = CreateResolver(temp.Info.FullName);

        Assert.ThrowsAny<ArgumentException>(() => resolver.GetMigrationPath(new MigrationSource.Project(""), "Test"));
    }

    [Fact]
    public void GetMigrationPath_EmptyName_Throws()
    {
        using TestDirectory temp = new();
        MigrationPathResolver resolver = CreateResolver(temp.Info.FullName);

        Assert.Throws<ArgumentException>(() => resolver.GetMigrationPath(new MigrationSource.Common(), ""));
    }
}
