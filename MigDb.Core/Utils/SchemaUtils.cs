using MigDb.Core.Schema;

namespace MigDb.Core.Utils;

public static class SchemaUtils
{
    public static string ToDropScript(SchemaObjectKind kind, string schema, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string keyword = kind switch
        {
            SchemaObjectKind.StoredProcedure => "PROCEDURE",
            SchemaObjectKind.View => "VIEW",
            SchemaObjectKind.Function => "FUNCTION",
            SchemaObjectKind.Trigger => "TRIGGER",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a programmable kind"),
        };

        return $"DROP {keyword} IF EXISTS [{schema}].[{name}];\n";
    }
}
