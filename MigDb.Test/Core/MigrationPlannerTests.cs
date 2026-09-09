using MigDb.Core.Migration;
using MigDb.Core.Migration.Manifest;
using MigDb.Core.Migration.Plan;
using MigDb.Core.Migration.Validation;
using MigDb.Test.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigDb.Test.Core;

public class MigrationPlannerTests
{
    private const string TestSql = "SELECT 1;";

    private static MigrationValidator CreateValidator()
    {
        MigrationHasher hasher = new(NullLogger<MigrationHasher>.Instance);
        MigrationManifestStore store = new(hasher, NullLogger<MigrationManifestStore>.Instance);

        return new MigrationValidator(hasher, store, NullLogger<MigrationValidator>.Instance);
    }

    private static MigrationPlanner CreatePlanner() => new(NullLogger<MigrationPlanner>.Instance, CreateValidator());

    private static MigrationDirectory CreateDirectory(TestDirectory temp) => new(temp.Info, new MigrationSource.Common());

    [Fact]
    public async Task CreateMigrationPlan_ContainsSubDirectory_Throws()
    {
        using TestDirectory temp = new();
        temp.CreateSubDirectory("Sample");
        temp.CreateSubDirectory("Sample2");

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.NotEmpty(temp.Info.GetDirectories());

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(() => planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct));

        Assert.Contains(MigrationValidationError.HasSubDirectories, ex.Errors);
    }

    [Fact]
    public async Task CreateMigrationPlan_EmptyDirectory_Throws()
    {
        using TestDirectory temp = new();
        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Empty(temp.Info.GetDirectories());

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(() => planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct));

        Assert.Contains(MigrationValidationError.Empty, ex.Errors);
    }

    [Fact]
    public async Task CreateMigrationPlan_TooManyFiles_Throws()
    {
        using TestDirectory temp = new();

        for (int i = 0; i < 100; ++i)
            temp.WriteFile($"{i + 1:D2}_file.sql", TestSql);

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;

        FileInfo[] files = temp.Info.GetFiles();

        Assert.Equal(100, files.Length);

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(() => planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct));

        Assert.Contains(MigrationValidationError.TooManyFiles, ex.Errors);
    }

    [Fact]
    public async Task CreateMigrationPlan_ContainsNonSQL_Throws()
    {
        using TestDirectory temp = new();
        temp.WriteFile("01_test.sql", TestSql);
        temp.WriteFile("test.txt", "not sql");

        Assert.NotEmpty(temp.Info.GetFiles());

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(() => planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct));

        Assert.Contains(MigrationValidationError.NonSqlFiles, ex.Errors);
    }

    [Fact]
    public async Task CreateMigrationPlan_InvalidSql_Throws()
    {
        using TestDirectory temp = new();
        temp.WriteFile("01_test.sql", "SELECT * FROM");

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(() => planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct));

        Assert.Contains(MigrationValidationError.SQLParseError, ex.Errors);
    }

    [Fact]
    public async Task CreateMigrationPlan_Sorted_HasPriority()
    {
        using TestDirectory temp = new();
        temp.WriteFile("03_test.sql", TestSql);
        temp.WriteFile("test.sql", TestSql);
        temp.WriteFile("12_orSomething.sql", TestSql);

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;
        MigrationPlan plan = await planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct);

        Assert.NotEmpty(plan.Files);
        Assert.Equal("03_test.sql", plan.Files[0].GeneratedName);
        Assert.Equal("12_orSomething.sql", plan.Files[1].GeneratedName);
        Assert.Equal("13_test.sql", plan.Files[2].GeneratedName);
    }

    [Fact]
    public async Task CreateMigrationPlan_NumberedFiles_KeepTheirNumbers()
    {
        using TestDirectory temp = new();
        temp.WriteFile("04_first.sql", TestSql);
        temp.WriteFile("07_second.sql", TestSql);

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;
        MigrationPlan plan = await planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct);

        Assert.Equal("04_first.sql", plan.Files[0].GeneratedName);
        Assert.Equal("07_second.sql", plan.Files[1].GeneratedName);
    }

    [Fact]
    public async Task ApplyMigrationPlan_NumberedFiles_NotRenamed()
    {
        using TestDirectory temp = new();
        temp.WriteFile("04_first.sql", TestSql);
        temp.WriteFile("07_second.sql", TestSql);
        temp.WriteFile("third.sql", TestSql);

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;
        MigrationPlan plan = await planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct);

        planner.ApplyMigrationPlan(plan, dryRun: false);

        string[] names = [.. temp.Info.GetFiles().Select(f => f.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(["04_first.sql", "07_second.sql", "08_third.sql"], names);
    }

    [Fact]
    public async Task ApplyMigrationPlan_SourceFile_Deleted()
    {
        string oName = "test.sql";
        string eName = "01_test.sql";

        using TestDirectory temp = new();
        temp.WriteFile(oName, TestSql);

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;
        MigrationPlan plan = await planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct);

        Assert.NotEmpty(plan.Files);

        planner.ApplyMigrationPlan(plan, dryRun: false);

        string oPath = Path.Join(temp.Info.FullName, oName);
        string ePath = Path.Join(temp.Info.FullName, eName);

        Assert.True(File.Exists(ePath));
        Assert.False(File.Exists(oPath));
    }

    [Fact]
    public async Task ApplyMigrationPlan_DryRun_NoFileChange()
    {
        string oName = "test.sql";
        string eName = "01_test.sql";

        using TestDirectory temp = new();
        temp.WriteFile(oName, TestSql);

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;
        MigrationPlan plan = await planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct);

        Assert.NotEmpty(plan.Files);

        planner.ApplyMigrationPlan(plan, dryRun: true);

        string oPath = Path.Join(temp.Info.FullName, oName);
        string ePath = Path.Join(temp.Info.FullName, eName);

        Assert.True(File.Exists(oPath));
        Assert.False(File.Exists(ePath));
    }

    [Fact]
    public async Task ApplyMigrationPlan_AlreadySortedFile_Stays()
    {
        using TestDirectory temp = new();
        temp.WriteFile("01_test.sql", TestSql);

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;
        MigrationPlan plan = await planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct);

        planner.ApplyMigrationPlan(plan, dryRun: false);

        string path = Path.Join(temp.Info.FullName, "01_test.sql");

        Assert.True(File.Exists(path));

        FileInfo file = temp.Info.GetFiles()[0];

        Assert.Equal("01_test.sql", file.Name);
    }
}
