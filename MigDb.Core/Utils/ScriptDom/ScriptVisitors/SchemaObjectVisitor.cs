using MigDb.Core.Schema;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MigDb.Core.Utils.ScriptDom.ScriptVisitors;

public sealed class SchemaObjectVisitor : TSqlFragmentVisitor
{
    public string? ObjectSchema { get; private set; }
    public string? ObjectName { get; private set; }
    public SchemaObjectKind? Kind { get; private set; }
    public SchemaObjectName? FirstTableRef { get; private set; }

    public override void Visit(CreateTableStatement node) => Set(node.SchemaObjectName, SchemaObjectKind.Table);
    public override void Visit(AlterTableStatement node) => Set(node.SchemaObjectName, SchemaObjectKind.Table);
    public override void Visit(CreateIndexStatement node) => Set(node.OnName, SchemaObjectKind.Table);
    public override void Visit(AlterIndexStatement node) => Set(node.OnName, SchemaObjectKind.Table);
    public override void Visit(CreateStatisticsStatement node) => Set(node.OnName, SchemaObjectKind.Table);
    public override void Visit(CreateSynonymStatement node) => Set(node.Name, SchemaObjectKind.Synonym);
    public override void Visit(DropSynonymStatement node) => Set(node.Objects[0], SchemaObjectKind.Synonym);
    public override void Visit(CreateTypeStatement node) => Set(node.Name, SchemaObjectKind.Type);
    public override void Visit(DropTypeStatement node) => Set(node.Name, SchemaObjectKind.Type);
    public override void Visit(DropSchemaStatement node) => Set(node.Schema, SchemaObjectKind.Schema);
    public override void Visit(DropTableStatement node) => Set(node.Objects[0], SchemaObjectKind.Table);
    public override void Visit(DropIndexStatement node)
    {
        foreach (DropIndexClauseBase clause in node.DropIndexClauses)
        {
            if (clause is DropIndexClause dropIndex)
            {
                Set(dropIndex.Object, SchemaObjectKind.Table);
                return;
            }

            if (clause is BackwardsCompatibleDropIndexClause legacy)
            {
                Set(legacy.Index, SchemaObjectKind.Table);
                return;
            }
        }
    }

    // Programmables - classify by the object being defined (a proc/view/function/trigger),
    // not the first table its body happens to reference.
    public override void Visit(CreateProcedureStatement node) => Set(node.ProcedureReference?.Name, SchemaObjectKind.StoredProcedure);
    public override void Visit(AlterProcedureStatement node) => Set(node.ProcedureReference?.Name, SchemaObjectKind.StoredProcedure);
    public override void Visit(CreateOrAlterProcedureStatement node) => Set(node.ProcedureReference?.Name, SchemaObjectKind.StoredProcedure);
    public override void Visit(CreateViewStatement node) => Set(node.SchemaObjectName, SchemaObjectKind.View);
    public override void Visit(AlterViewStatement node) => Set(node.SchemaObjectName, SchemaObjectKind.View);
    public override void Visit(CreateOrAlterViewStatement node) => Set(node.SchemaObjectName, SchemaObjectKind.View);
    public override void Visit(CreateFunctionStatement node) => Set(node.Name, SchemaObjectKind.Function);
    public override void Visit(AlterFunctionStatement node) => Set(node.Name, SchemaObjectKind.Function);
    public override void Visit(CreateOrAlterFunctionStatement node) => Set(node.Name, SchemaObjectKind.Function);
    public override void Visit(CreateTriggerStatement node) => Set(node.Name, SchemaObjectKind.Trigger);
    public override void Visit(AlterTriggerStatement node) => Set(node.Name, SchemaObjectKind.Trigger);
    public override void Visit(CreateOrAlterTriggerStatement node) => Set(node.Name, SchemaObjectKind.Trigger);
    public override void Visit(DropProcedureStatement node) => Set(node.Objects[0], SchemaObjectKind.StoredProcedure);
    public override void Visit(DropViewStatement node) => Set(node.Objects[0], SchemaObjectKind.View);
    public override void Visit(DropFunctionStatement node) => Set(node.Objects[0], SchemaObjectKind.Function);
    public override void Visit(DropTriggerStatement node) => Set(node.Objects[0], SchemaObjectKind.Trigger);

    public override void Visit(NamedTableReference node) => FirstTableRef ??= node.SchemaObject;

    public override void Visit(CreateSchemaStatement node)
    {
        if (Kind is not null || node.Name is null)
            return;

        ObjectSchema = null;
        ObjectName = node.Name.Value;
        Kind = SchemaObjectKind.Schema;
    }

    private void Set(SchemaObjectName? name, SchemaObjectKind kind)
    {
        if (Kind is not null || name is null)
            return;

        ObjectSchema = name.SchemaIdentifier?.Value;
        ObjectName = name.BaseIdentifier.Value;
        Kind = kind;
    }
}