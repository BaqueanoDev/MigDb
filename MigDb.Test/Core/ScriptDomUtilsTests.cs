using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;
using MigDb.Core.Utils;
using MigDb.Core.Utils.ScriptDom;
using MigDb.Test.Utils;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MigDb.Test.Core;

public class ScriptDomUtilsTests
{
    [Fact]
    public void Parse_ValidFile_NoErrors()
    {
        using TestDirectory temp = new();

        FileInfo file = new(temp.WriteFile("valid.sql", "SELECT 1;"));

        IReadOnlyList<ParseError> errors = ScriptDomUtils.Parse(file);

        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_InvalidFile_ReturnErrors()
    {
        using TestDirectory temp = new();

        FileInfo file = new(temp.WriteFile("invalid.sql", "SELECT * FROM"));

        IReadOnlyList<ParseError> errors = ScriptDomUtils.Parse(file);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Parse_ValidFiles_ParseMultiple()
    {
        using TestDirectory temp = new();

        temp.WriteFile("01_valid1.sql", "SELECT 1;");
        temp.WriteFile("02_valid2.sql", "CREATE TABLE dbo.Foo (Id INT NOT NULL);");

        IReadOnlyList<ParseError> errors = ScriptDomUtils.Parse(temp.Info);

        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_FileMix_ReturnErrors()
    {
        using TestDirectory temp = new();

        temp.WriteFile("01_valid.sql", "SELECT 1;");
        temp.WriteFile("02_invalid.sql", "SELECT * FROM");

        IReadOnlyList<ParseError> errors = ScriptDomUtils.Parse(temp.Info);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ContainsGoBatch_NoGo_False()
    {
        Assert.False(ScriptDomUtils.ContainsBatchToken("SELECT 1;\nSELECT 2;"));
    }

    [Fact]
    public void ContainsGoBatch_WithGo_True()
    {
        string sql = "CREATE TABLE dbo.A (Id INT);\nGO\nCREATE TABLE dbo.B (Id INT);";

        Assert.True(ScriptDomUtils.ContainsBatchToken(sql));
    }

    [Fact]
    public void ContainsGoBatch_GoInsideString_False()
    {
        Assert.False(ScriptDomUtils.ContainsBatchToken("SELECT 'GO';"));
    }

    [Fact]
    public void ContainsGoBatch_Empty_False()
    {
        Assert.False(ScriptDomUtils.ContainsBatchToken("   \n  "));
    }

    [Fact]
    public void FindTokens_EmptyBlacklist_ReturnsEmpty()
    {
        Assert.Empty(ScriptDomUtils.FindTokens("CREATE TABLE dbo.A (Id INT);\nGO\nSELECT 1;", []));
    }

    [Fact]
    public void FindTokens_PresentAndAbsent_ReturnsOnlyPresent()
    {
        IReadOnlyList<TSqlTokenType> found = ScriptDomUtils.FindTokens(
            "SELECT 1;\nGO\nSELECT 2;",
            [TSqlTokenType.Go, TSqlTokenType.Merge]);

        Assert.Equal([TSqlTokenType.Go], found);
    }

    [Fact]
    public void FindTokens_RepeatedToken_Deduplicates()
    {
        IReadOnlyList<TSqlTokenType> found = ScriptDomUtils.FindTokens(
            "SELECT 1;\nGO\nSELECT 2;\nGO\nSELECT 3;",
            [TSqlTokenType.Go]);

        Assert.Single(found);
    }

    [Fact]
    public void GetTargetObject_GuardWithSubquery_FallsBackToReferencedTable()
    {
        const string guard = """
            IF EXISTS (select top 1 1 from [dbo].[Member])
                RAISERROR (N'Rows were detected.', 16, 127) WITH NOWAIT
            """;

        SchemaObject? target = ScriptDomUtils.GetSchemaObject(guard);

        Assert.Equal(SchemaObjectKind.Table, target?.Kind);
        Assert.Equal("dbo.Member", target?.FullName);
    }

    [Fact]
    public void GetTargetObject_UnqualifiedTable_DefaultsToDbo()
    {
        SchemaObject? target = ScriptDomUtils.GetSchemaObject("ALTER TABLE Foo ADD Bar INT NULL;");

        Assert.Equal("dbo.Foo", target?.FullName);
    }

    [Fact]
    public void GetTargetObject_Unparseable_ReturnsNull()
    {
        Assert.Null(ScriptDomUtils.GetSchemaObject("ALTER TABLE"));
    }

    [Fact]
    public void GetTargetObject_AlterTable_ClassifiesAsTable()
    {
        SchemaObject? target = ScriptDomUtils.GetSchemaObject("ALTER TABLE [dbo].[Campus] ADD [TestA] BIT NULL;");

        Assert.Equal(SchemaObjectKind.Table, target?.Kind);
        Assert.Equal("dbo.Campus", target?.FullName);
    }

    [Fact]
    public void GetTargetObject_CreateSchema_ClassifiesAsSchema()
    {
        SchemaObject? target = ScriptDomUtils.GetSchemaObject("CREATE SCHEMA [client];");

        Assert.Equal(SchemaObjectKind.Schema, target?.Kind);
        // schema objects have no containing schema
        Assert.Equal("client", target?.FullName);
    }

    [Fact]
    public void GetTargetObject_CreateSynonym_ClassifiesAsSynonym()
    {
        SchemaObject? target = ScriptDomUtils.GetSchemaObject("CREATE SYNONYM [client].[Staging] FOR [client].[StagingData];");

        Assert.Equal(SchemaObjectKind.Synonym, target?.Kind);
        Assert.Equal("client.Staging", target?.FullName);
    }

    [Fact]
    public void GetTargetObject_CreateTableType_ClassifiesAsType()
    {
        const string sql = "CREATE TYPE [dbo].[Type_Budget] AS TABLE ([Id] INT NULL);";

        SchemaObject? target = ScriptDomUtils.GetSchemaObject(sql);

        Assert.Equal(SchemaObjectKind.Type, target?.Kind);
        Assert.Equal("dbo.Type_Budget", target?.FullName);
    }

    [Fact]
    public void GetTargetObject_IndexOnTable_FallsBackToTable()
    {
        SchemaObject? target = ScriptDomUtils.GetSchemaObject("CREATE INDEX IX_Foo ON [dbo].[Foo] ([Bar]);");

        Assert.Equal(SchemaObjectKind.Table, target?.Kind);
        Assert.Equal("dbo.Foo", target?.FullName);
    }

    [Fact]
    public void GetTargetObject_NoObject_ReturnsNull()
    {
        Assert.Null(ScriptDomUtils.GetSchemaObject("PRINT N'hello';"));
    }

    [Theory]
    // the modern spelling, which is what DacFx emits when an index is removed or redefined
    [InlineData("DROP INDEX [IX_Foo] ON [rpt].[Foo];")]
    // ... and the legacy one, where the index is the trailing part of the name
    [InlineData("DROP INDEX [rpt].[Foo].[IX_Foo];")]
    public void GetTargetObject_DropIndex_ClassifiesAsOwningTable(string sql)
    {
        SchemaObject? target = ScriptDomUtils.GetSchemaObject(sql);

        Assert.Equal(SchemaObjectKind.Table, target?.Kind);
        Assert.Equal("rpt.Foo", target?.FullName);
    }

    [Theory]
    [InlineData("DROP TABLE [dbo].[Foo];", SchemaObjectKind.Table, "dbo.Foo")]
    [InlineData("DROP VIEW [rpt].[V_Foo];", SchemaObjectKind.View, "rpt.V_Foo")]
    [InlineData("DROP PROCEDURE [dbo].[USP_Foo];", SchemaObjectKind.StoredProcedure, "dbo.USP_Foo")]
    [InlineData("DROP FUNCTION [dbo].[FN_Foo];", SchemaObjectKind.Function, "dbo.FN_Foo")]
    [InlineData("DROP TRIGGER [dbo].[TR_Foo];", SchemaObjectKind.Trigger, "dbo.TR_Foo")]
    [InlineData("DROP SYNONYM [client].[Staging];", SchemaObjectKind.Synonym, "client.Staging")]
    public void GetTargetObject_DropObject_ClassifiesObject(string sql, SchemaObjectKind kind, string fullName)
    {
        SchemaObject? target = ScriptDomUtils.GetSchemaObject(sql);

        Assert.Equal(kind, target?.Kind);
        Assert.Equal(fullName, target?.FullName);
    }

    [Fact]
    public void GetTargetObject_DropTableList_TakesFirst()
    {
        SchemaObject? target = ScriptDomUtils.GetSchemaObject("DROP TABLE [dbo].[Foo], [dbo].[Bar];");

        Assert.Equal("dbo.Foo", target?.FullName);
    }

    [Theory]
    [InlineData("CREATE PROCEDURE [dbo].[USP_Foo] AS SELECT 1;", SchemaObjectKind.StoredProcedure, "dbo.USP_Foo")]
    [InlineData("CREATE VIEW [rpt].[V_Foo] AS SELECT 1 AS [A];", SchemaObjectKind.View, "rpt.V_Foo")]
    [InlineData("CREATE FUNCTION [dbo].[FN_Foo] () RETURNS INT AS BEGIN RETURN 1; END", SchemaObjectKind.Function, "dbo.FN_Foo")]
    [InlineData("CREATE TRIGGER [dbo].[TR_Foo] ON [dbo].[Foo] AFTER INSERT AS SELECT 1;", SchemaObjectKind.Trigger, "dbo.TR_Foo")]
    public void GetProgrammable_PlainCreate_ClassifiesObject(string sql, SchemaObjectKind kind, string fullName)
    {
        SchemaProgrammableDefinition? programmable = ScriptDomUtils.GetProgrammable(sql);

        Assert.Equal(kind, programmable?.Object.Kind);
        Assert.Equal(fullName, programmable?.Object.FullName);
        Assert.Equal(SchemaProgrammableExclusion.None, programmable?.Exclusion);
    }

    [Fact]
    public void GetProgrammable_UnqualifiedProcedure_DefaultsToDbo()
    {
        SchemaProgrammableDefinition? programmable = ScriptDomUtils.GetProgrammable("CREATE PROCEDURE USP_Foo AS SELECT 1;");

        Assert.Equal("dbo.USP_Foo", programmable?.Object.FullName);
    }

    [Fact]
    public void GetProgrammable_NonProgrammable_ReturnsNull()
    {
        Assert.Null(ScriptDomUtils.GetProgrammable("CREATE TABLE dbo.Foo (Id INT NOT NULL);"));
    }

    [Theory]
    [InlineData("CREATE FUNCTION [dbo].[FN_Foo] () RETURNS INT WITH SCHEMABINDING AS BEGIN RETURN 1; END")]
    [InlineData("CREATE VIEW [rpt].[V_Foo] WITH SCHEMABINDING AS SELECT 1 AS [A];")]
    public void GetProgrammable_SchemaBound_IsExcluded(string sql)
    {
        SchemaProgrammableDefinition? programmable = ScriptDomUtils.GetProgrammable(sql);

        Assert.Equal(SchemaProgrammableExclusion.SchemaBound, programmable?.Exclusion);
    }

    [Fact]
    public void GetProgrammable_IndexedView_IsExcludedAsIndexedView()
    {
        // an indexed view is schema-bound as well, but losing the index to an ALTER is the costlier problem
        string sql = """
            CREATE VIEW [rpt].[V_Foo] WITH SCHEMABINDING AS SELECT COUNT_BIG(*) AS [C], [A] FROM [dbo].[Foo] GROUP BY [A];
            GO
            CREATE UNIQUE CLUSTERED INDEX [IX_V_Foo] ON [rpt].[V_Foo] ([A] ASC);
            """;

        SchemaProgrammableDefinition? programmable = ScriptDomUtils.GetProgrammable(sql);

        Assert.Equal(SchemaProgrammableExclusion.IndexedView, programmable?.Exclusion);
        Assert.Equal("rpt.V_Foo", programmable?.Object.FullName);
    }

    [Theory]
    [InlineData("CREATE PROCEDURE [dbo].[USP_Foo] AS SELECT 1;", "CREATE OR ALTER PROCEDURE [dbo].[USP_Foo] AS SELECT 1;")]
    [InlineData("CREATE VIEW [rpt].[V_Foo] AS SELECT 1 AS [A];", "CREATE OR ALTER VIEW [rpt].[V_Foo] AS SELECT 1 AS [A];")]
    [InlineData("CREATE FUNCTION [dbo].[FN_Foo] () RETURNS INT AS BEGIN RETURN 1; END", "CREATE OR ALTER FUNCTION [dbo].[FN_Foo] () RETURNS INT AS BEGIN RETURN 1; END")]
    [InlineData("CREATE TRIGGER [dbo].[TR_Foo] ON [dbo].[Foo] AFTER INSERT AS SELECT 1;", "CREATE OR ALTER TRIGGER [dbo].[TR_Foo] ON [dbo].[Foo] AFTER INSERT AS SELECT 1;")]
    [InlineData("create proc dbo.USP_Foo as select 1;", "create OR ALTER proc dbo.USP_Foo as select 1;")]
    public void ToCreateOrAlter_Create_RewritesDeclaration(string sql, string expected)
    {
        Assert.Equal(expected, ScriptDomUtils.ToCreateOrAlter(sql));
    }

    [Fact]
    public void ToCreateOrAlter_LeadingCommentsAndBody_PreservedVerbatim()
    {
        const string sql = """
            -- header comment mentioning CREATE
            /* block */
            CREATE PROCEDURE [dbo].[USP_Foo]
                @Id INT
            AS
            BEGIN
                -- inner CREATE reference
                CREATE TABLE #Tmp (Id INT);
                SELECT @Id;
            END
            """;

        const string expected = """
            -- header comment mentioning CREATE
            /* block */
            CREATE OR ALTER PROCEDURE [dbo].[USP_Foo]
                @Id INT
            AS
            BEGIN
                -- inner CREATE reference
                CREATE TABLE #Tmp (Id INT);
                SELECT @Id;
            END
            """;

        Assert.Equal(expected, ScriptDomUtils.ToCreateOrAlter(sql));
    }

    [Fact]
    public void ToCreateOrAlter_TrailingBatch_RewritesCreateAndPreservesRest()
    {
        const string sql = """
            CREATE TRIGGER [dbo].[tr_Foo_Audit] ON [dbo].[Foo]
                AFTER INSERT, UPDATE
            AS
                BEGIN
                    UPDATE Foo SET DbTime = GETDATE() FROM inserted;
                END
            ;
            GO
            ALTER TABLE [dbo].[Foo] DISABLE TRIGGER [tr_Foo_Audit]
            """;

        const string expected = """
            CREATE OR ALTER TRIGGER [dbo].[tr_Foo_Audit] ON [dbo].[Foo]
                AFTER INSERT, UPDATE
            AS
                BEGIN
                    UPDATE Foo SET DbTime = GETDATE() FROM inserted;
                END
            ;
            GO
            ALTER TABLE [dbo].[Foo] DISABLE TRIGGER [tr_Foo_Audit]
            """;

        Assert.Equal(expected, ScriptDomUtils.ToCreateOrAlter(sql));
    }

    [Fact]
    public void ToCreateOrAlter_TrailingBatch_SplitsIntoCreateThenState()
    {
        const string sql = """
            CREATE TRIGGER [dbo].[tr_Foo_Audit] ON [dbo].[Foo] AFTER INSERT AS SELECT 1;
            GO
            ALTER TABLE [dbo].[Foo] DISABLE TRIGGER [tr_Foo_Audit]
            """;

        IReadOnlyList<string> batches = DacFxUtils.SplitScriptByBatch(ScriptDomUtils.ToCreateOrAlter(sql));

        Assert.Equal(2, batches.Count);
        Assert.Contains("CREATE OR ALTER TRIGGER", batches[0]);
        Assert.DoesNotContain("DISABLE TRIGGER", batches[0]);
        Assert.Contains("DISABLE TRIGGER", batches[1]);
    }

    [Fact]
    public void ToCreateOrAlter_Rewritten_IsValidSql()
    {
        string sql = ScriptDomUtils.ToCreateOrAlter("CREATE PROCEDURE [dbo].[USP_Foo] AS SELECT 1;");

        Assert.Empty(ScriptDomUtils.Parse(sql));
    }

    [Fact]
    public void ToCreateOrAlter_AlreadyOrAlter_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ScriptDomUtils.ToCreateOrAlter("CREATE OR ALTER PROCEDURE [dbo].[USP_Foo] AS SELECT 1;"));
    }

    [Fact]
    public void ToCreateOrAlter_NonProgrammable_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ScriptDomUtils.ToCreateOrAlter("CREATE TABLE dbo.Foo (Id INT NOT NULL);"));
    }

    [Fact]
    public void ToCreateOrAlter_ParseError_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ScriptDomUtils.ToCreateOrAlter("CREATE PROCEDURE"));
    }
}
