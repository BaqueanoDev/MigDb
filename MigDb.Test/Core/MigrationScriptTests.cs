using MigDb.Core.Migration.Generate;
using MigDb.Core.Schema;

namespace MigDb.Test.Core;

public class MigrationScriptTests
{
    private static MigrationScript Script(string schema, string name, SchemaObjectKind kind, int sequence)
    {
        return new MigrationScript(new SchemaObject(schema, name, kind), "SELECT 1;", [], sequence);
    }

    [Fact]
    public void FileName_CarriesSequenceKindAndFullName()
    {
        MigrationScript script = Script("dbo", "MemberRole", SchemaObjectKind.Table, 1);

        Assert.Equal("01_Table_dbo.MemberRole.sql", script.FileName);
    }

    [Fact]
    public void FileName_PadsSequenceSoItSortsOrdinally()
    {
        string second = Script("dbo", "MemberRole", SchemaObjectKind.Table, 2).FileName;
        string tenth = Script("dbo", "MemberRole", SchemaObjectKind.Table, 10).FileName;

        Assert.True(string.CompareOrdinal(second, tenth) < 0);
    }

    [Fact]
    public void FileName_SameObjectAcrossBatches_ProducesDistinctNames()
    {
        string first = Script("dbo", "MemberRole", SchemaObjectKind.Table, 1).FileName;
        string second = Script("dbo", "MemberRole", SchemaObjectKind.Table, 2).FileName;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void FileName_SchemaObject_HasNoLeadingDot()
    {
        MigrationScript script = Script(string.Empty, "rpt", SchemaObjectKind.Schema, 3);

        Assert.Equal("03_Schema_rpt.sql", script.FileName);
    }
}
