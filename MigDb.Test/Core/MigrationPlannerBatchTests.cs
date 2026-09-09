using MigDb.Core.Migration;
using MigDb.Core.Migration.Manifest;
using MigDb.Core.Migration.Plan;
using MigDb.Core.Migration.Validation;
using MigDb.Test.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace MigDb.Test.Core;

/// <summary>
/// Prepare splitting a file that carries batch separators into one file per batch.
/// </summary>
public class MigrationPlannerBatchTests
{
    private static MigrationValidator CreateValidator()
    {
        MigrationHasher hasher = new(NullLogger<MigrationHasher>.Instance);
        MigrationManifestStore store = new(hasher, NullLogger<MigrationManifestStore>.Instance);

        return new MigrationValidator(hasher, store, NullLogger<MigrationValidator>.Instance);
    }

    private static MigrationPlanner CreatePlanner() => new(NullLogger<MigrationPlanner>.Instance, CreateValidator());

    private static MigrationDirectory CreateDirectory(TestDirectory temp) => new(temp.Info, new MigrationSource.Common());

    private static string[] DataFileNames(TestDirectory temp) => [.. CreateDirectory(temp).GetDataFiles().Select(f => f.Name)];

    private static async Task<string[]> PrepareAsync(TestDirectory temp, bool splitBatches = true)
    {
        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;

        MigrationPlan plan = await planner.CreateMigrationPlanAsync(CreateDirectory(temp), splitBatches, ct);
        planner.ApplyMigrationPlan(plan);

        return DataFileNames(temp);
    }

    [Fact]
    public async Task NoBatchToken_FileTakenAsIs()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;");

        MigrationPlan plan = await CreatePlanner().CreateMigrationPlanAsync(CreateDirectory(temp), ct: TestContext.Current.CancellationToken);

        MigrationPlanFile file = Assert.Single(plan.Files);

        Assert.IsType<MigrationPlanFile.Copy>(file);
        Assert.Equal("01_Foo.sql", file.GeneratedName);
    }

    [Fact]
    public async Task MultipleBatches_OnePlanFilePerBatch()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;\nGO\nSELECT 2;\nGO\nSELECT 3;");

        MigrationPlan plan = await CreatePlanner().CreateMigrationPlanAsync(CreateDirectory(temp), ct: TestContext.Current.CancellationToken);

        Assert.Equal(["01_Foo_01.sql", "01_Foo_02.sql", "01_Foo_03.sql"], plan.Files.Select(f => f.GeneratedName));
        Assert.Equal(["SELECT 1;", "SELECT 2;", "SELECT 3;"], plan.Files.Select(f => Assert.IsType<MigrationPlanFile.Batch>(f).Content));

        // every part still points at the file it came from, which is what apply deletes
        Assert.All(plan.Files, f => Assert.Equal("Foo.sql", f.File.Name));
    }

    [Fact]
    public async Task TrailingSeparatorOnly_KeepsOneFile()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;\nGO\n");

        MigrationPlan plan = await CreatePlanner().CreateMigrationPlanAsync(CreateDirectory(temp), ct: TestContext.Current.CancellationToken);

        MigrationPlanFile file = Assert.Single(plan.Files);

        Assert.Equal("01_Foo.sql", file.GeneratedName);
        Assert.Equal("SELECT 1;", Assert.IsType<MigrationPlanFile.Batch>(file).Content);
    }

    [Fact]
    public async Task ParseErrorInBatch_ThrowsAndLeavesFileIntact()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;\nGO\nTHIS IS NOT SQL(((;");

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(
            () => CreatePlanner().CreateMigrationPlanAsync(CreateDirectory(temp), ct: TestContext.Current.CancellationToken));

        Assert.Contains(MigrationValidationError.SQLParseError, ex.Errors);
        Assert.Equal(["Foo.sql"], DataFileNames(temp));
    }

    [Fact]
    public async Task PartWouldOverwriteExistingFile_Throws()
    {
        using TestDirectory temp = new();
        temp.WriteFile("01_Foo.sql", "SELECT 1;\nGO\nSELECT 2;");
        temp.WriteFile("01_Foo_01.sql", "SELECT 3;");

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(
            () => CreatePlanner().CreateMigrationPlanAsync(CreateDirectory(temp), ct: TestContext.Current.CancellationToken));

        Assert.Contains(MigrationValidationError.GeneratedNameCollision, ex.Errors);
        Assert.Equal(["01_Foo.sql", "01_Foo_01.sql"], DataFileNames(temp));
    }

    [Fact]
    public async Task BatchesPastFileCeiling_Throws()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", string.Join("\nGO\n", Enumerable.Repeat("SELECT 1;", 100)));

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(
            () => CreatePlanner().CreateMigrationPlanAsync(CreateDirectory(temp), ct: TestContext.Current.CancellationToken));

        Assert.Contains(MigrationValidationError.TooManyFiles, ex.Errors);
    }

    [Fact]
    public async Task SplitDisabled_BatchTokenStillRejected()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;\nGO\nSELECT 2;");

        MigrationValidationException ex = await Assert.ThrowsAsync<MigrationValidationException>(
            () => CreatePlanner().CreateMigrationPlanAsync(CreateDirectory(temp), splitBatches: false, TestContext.Current.CancellationToken));

        Assert.Contains(MigrationValidationError.ContainsBatchToken, ex.Errors);
        Assert.Equal(["Foo.sql"], DataFileNames(temp));
    }

    [Fact]
    public async Task Apply_ReplacesSourceWithItsBatches()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;\nGO\nSELECT 2;");

        string[] names = await PrepareAsync(temp);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Equal(["01_Foo_01.sql", "01_Foo_02.sql"], names);
        Assert.Equal("SELECT 1;", await File.ReadAllTextAsync(Path.Join(temp.Info.FullName, "01_Foo_01.sql"), ct));
        Assert.Equal("SELECT 2;", await File.ReadAllTextAsync(Path.Join(temp.Info.FullName, "01_Foo_02.sql"), ct));
    }

    [Fact]
    public async Task Apply_TrailingSeparatorOnAlreadyNumberedFile_RewritesInPlace()
    {
        using TestDirectory temp = new();
        temp.WriteFile("01_Foo.sql", "SELECT 1;\nGO\n");

        string[] names = await PrepareAsync(temp);

        Assert.Equal(["01_Foo.sql"], names);
        Assert.Equal("SELECT 1;", await File.ReadAllTextAsync(Path.Join(temp.Info.FullName, "01_Foo.sql"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_DryRun_TouchesNothing()
    {
        using TestDirectory temp = new();
        const string content = "SELECT 1;\nGO\nSELECT 2;";
        temp.WriteFile("Foo.sql", content);

        MigrationPlanner planner = CreatePlanner();
        CancellationToken ct = TestContext.Current.CancellationToken;

        MigrationPlan plan = await planner.CreateMigrationPlanAsync(CreateDirectory(temp), ct: ct);
        planner.ApplyMigrationPlan(plan, dryRun: true);

        Assert.Equal(["Foo.sql"], DataFileNames(temp));
        Assert.Equal(content, await File.ReadAllTextAsync(Path.Join(temp.Info.FullName, "Foo.sql"), ct));
    }

    [Fact]
    public async Task Apply_BatchesRunBeforeLaterFiles()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;\nGO\nSELECT 2;\nGO\nSELECT 3;");
        temp.WriteFile("Zed.sql", "SELECT 4;");

        Assert.Equal(["01_Foo_01.sql", "01_Foo_02.sql", "01_Foo_03.sql", "02_Zed.sql"], await PrepareAsync(temp));
    }

    [Fact]
    public async Task Apply_NumberedSource_BatchesKeepItsPosition()
    {
        using TestDirectory temp = new();
        temp.WriteFile("01_Aaa.sql", "SELECT 1;");
        temp.WriteFile("02_Bbb.sql", "SELECT 2;\nGO\nSELECT 3;");
        temp.WriteFile("03_Ccc.sql", "SELECT 4;");

        // the batches take the slot the file they came from held, and still run between 01 and 03
        Assert.Equal(["01_Aaa.sql", "02_Bbb_01.sql", "02_Bbb_02.sql", "03_Ccc.sql"], await PrepareAsync(temp));
    }

    [Fact]
    public async Task Prepare_AfterSplit_IsStable()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;\nGO\nSELECT 2;");

        for (int i = 0; i < 3; i++)
            await PrepareAsync(temp);

        Assert.Equal(["01_Foo_01.sql", "01_Foo_02.sql"], DataFileNames(temp));
    }

    [Fact]
    public async Task Prepare_SplitOutput_PassesValidation()
    {
        using TestDirectory temp = new();
        temp.WriteFile("Foo.sql", "SELECT 1;\nGO\nSELECT 2;");

        await PrepareAsync(temp);

        MigrationValidator validator = CreateValidator();
        CancellationToken ct = TestContext.Current.CancellationToken;

        foreach (FileInfo f in CreateDirectory(temp).GetDataFiles())
            Assert.True((await validator.ValidateFileAsync(f, ct)).IsValid, f.Name);
    }
}
