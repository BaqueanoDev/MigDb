using MigDb.Core.Schema;

namespace MigDb.Test.Core;

public class SchemaRevisionTests
{
    [Theory]
    [InlineData("working")]
    [InlineData("Working")]
    [InlineData("WORKING")]
    [InlineData("  working  ")]
    public void Parse_WorkingTreeToken_ReturnsWorkingTree(string spec)
    {
        SchemaRevision revision = SchemaRevision.Parse(spec);

        Assert.IsType<SchemaRevision.WorkingTree>(revision);
    }

    [Theory]
    [InlineData("HEAD", "HEAD")]
    [InlineData("vOld", "vOld")]
    [InlineData("HEAD~3", "HEAD~3")]
    [InlineData("  9c84ed70  ", "9c84ed70")]
    public void Parse_Commitish_ReturnsHistory(string spec, string expected)
    {
        SchemaRevision revision = SchemaRevision.Parse(spec);

        SchemaRevision.History history = Assert.IsType<SchemaRevision.History>(revision);
        Assert.Equal(expected, history.Commitish);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Blank_Throws(string spec)
    {
        Assert.ThrowsAny<ArgumentException>(() => SchemaRevision.Parse(spec));
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => SchemaRevision.Parse(null!));
    }

    [Fact]
    public void Describe_WorkingTree_ReadableLabel()
    {
        Assert.Equal("working tree", SchemaRevision.Describe(new SchemaRevision.WorkingTree()));
    }

    [Fact]
    public void Describe_History_UsesCommitish()
    {
        Assert.Equal("vOld", SchemaRevision.Describe(new SchemaRevision.History("vOld")));
    }

    // the generate command rejects a comparison of a revision against itself, which leans
    // on record equality holding for equivalent specs
    [Theory]
    [InlineData("HEAD", "HEAD")]
    [InlineData("HEAD", "  HEAD  ")]
    [InlineData("vOld", "vOld")]
    [InlineData("working", "WORKING")]
    public void Parse_EquivalentSpecs_AreEqual(string a, string b)
    {
        Assert.Equal(SchemaRevision.Parse(a), SchemaRevision.Parse(b));
    }

    [Theory]
    [InlineData("HEAD", "vOld")]
    [InlineData("working", "HEAD")]
    [InlineData("HEAD", "HEAD~1")]
    public void Parse_DifferentSpecs_AreNotEqual(string a, string b)
    {
        Assert.NotEqual(SchemaRevision.Parse(a), SchemaRevision.Parse(b));
    }
}
