using MigDb.Core.Schema;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MigDb.Core.Utils.ScriptDom.ScriptVisitors;

public sealed class CreateProgrammableVisitor : TSqlFragmentVisitor
{
    public SchemaObjectName? Name { get; private set; }
    public SchemaObjectKind? Kind { get; private set; }
    public TSqlStatement? CreateStatement { get; private set; }

    public bool SchemaBound { get; private set; }
    public bool Indexed { get; private set; }

    public override void Visit(CreateProcedureStatement node) => Capture(node, node.ProcedureReference.Name, SchemaObjectKind.StoredProcedure);
    public override void Visit(CreateTriggerStatement node) => Capture(node, node.Name, SchemaObjectKind.Trigger);

    public override void Visit(CreateViewStatement node)
    {
        foreach (ViewOption option in node.ViewOptions)
        {
            if (option.OptionKind == ViewOptionKind.SchemaBinding)
                SchemaBound = true;
        }

        Capture(node, node.SchemaObjectName, SchemaObjectKind.View);
    }

    public override void Visit(CreateFunctionStatement node)
    {
        foreach (FunctionOption option in node.Options)
        {
            if (option.OptionKind == FunctionOptionKind.SchemaBinding)
                SchemaBound = true;
        }

        Capture(node, node.Name, SchemaObjectKind.Function);
    }

    public override void Visit(CreateIndexStatement node) => Indexed = true;

    public override void Visit(CreateColumnStoreIndexStatement node) => Indexed = true;

    private void Capture(TSqlStatement createStatement, SchemaObjectName name, SchemaObjectKind kind)
    {
        if (Name is not null)
            return;

        CreateStatement = createStatement;
        Name = name;
        Kind = kind;
    }
}
