using MigDb.Core.Schema;
using MigDb.Core.Utils;

namespace MigDb.Test.Core;

public class SchemaUtilsTests
{
    [Theory]
    [InlineData(SchemaObjectKind.StoredProcedure, "DROP PROCEDURE IF EXISTS [dbo].[Thing];\n")]
    [InlineData(SchemaObjectKind.View, "DROP VIEW IF EXISTS [dbo].[Thing];\n")]
    [InlineData(SchemaObjectKind.Function, "DROP FUNCTION IF EXISTS [dbo].[Thing];\n")]
    [InlineData(SchemaObjectKind.Trigger, "DROP TRIGGER IF EXISTS [dbo].[Thing];\n")]
    public void ToDropScript_ProgrammableKind_ExpectedDdl(SchemaObjectKind kind, string expected)
    {
        Assert.Equal(expected, SchemaUtils.ToDropScript(kind, "dbo", "Thing"));
    }

    [Fact]
    public void ToDropScript_NonDboSchema_Bracketed()
    {
        Assert.Equal("DROP VIEW IF EXISTS [rpt].[Thing];\n", SchemaUtils.ToDropScript(SchemaObjectKind.View, "rpt", "Thing"));
    }

    [Theory]
    [InlineData(SchemaObjectKind.Table)]
    [InlineData(SchemaObjectKind.Synonym)]
    [InlineData(SchemaObjectKind.Type)]
    [InlineData(SchemaObjectKind.Schema)]
    public void ToDropScript_NonProgrammableKind_Throws(SchemaObjectKind kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SchemaUtils.ToDropScript(kind, "dbo", "Thing"));
    }

    [Theory]
    [InlineData("", "Thing")]
    [InlineData("dbo", "")]
    [InlineData("   ", "Thing")]
    public void ToDropScript_BlankIdentifier_Throws(string schema, string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => SchemaUtils.ToDropScript(SchemaObjectKind.View, schema, name));
    }
}
