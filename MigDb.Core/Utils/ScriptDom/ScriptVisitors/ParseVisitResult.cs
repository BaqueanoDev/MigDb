using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MigDb.Core.Utils.ScriptDom.ScriptVisitors;

public sealed record ParseVisitResult<TVisitor>(TVisitor Visitor, IReadOnlyList<ParseError> Errors);