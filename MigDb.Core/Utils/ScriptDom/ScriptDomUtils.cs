using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;
using MigDb.Core.Utils.ScriptDom.ScriptVisitors;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MigDb.Core.Utils.ScriptDom;

public static class ScriptDomUtils
{
    private static TSql160Parser CreateParser()
    {
        return new(initialQuotedIdentifiers: true);
    }

    public static IReadOnlyList<ParseError> Parse(TextReader reader)
    {
        List<ParseError> result = [];
        TSql160Parser parser = CreateParser();

        parser.Parse(reader, out IList<ParseError> errors);
        result.AddRange(errors);

        return result;
    }

    public static IReadOnlyList<ParseError> Parse(DirectoryInfo directory)
    {
        FileInfo[] files = directory.GetFiles();

        return Parse(files);
    }

    public static IReadOnlyList<ParseError> Parse(FileInfo file)
    {
        return Parse([file]);
    }

    public static IReadOnlyList<ParseError> Parse(string sql)
    {
        using StringReader reader = new(sql);
        IReadOnlyList<ParseError> result = Parse(reader);
        return result;
    }

    /// <summary>
    /// Parse files and return list of parse errors
    /// 
    /// TODO: Revisit, currently no way to corrolate errors to a file, new return type that links the two?
    /// </summary>
    /// <param name="files"></param>
    /// <returns>List of parsed errors</returns>
    public static IReadOnlyList<ParseError> Parse(IReadOnlyList<FileInfo> files)
    {
        List<ParseError> result = [];

        foreach (FileInfo f in files)
        {
            using StreamReader reader = new(f.FullName);

            IReadOnlyList<ParseError> errors = Parse(reader);
            result.AddRange(errors);
        }

        return result;
    }

    /// <summary>
    /// Checks script for a specific TSqlTokenType
    /// </summary>
    /// <param name="sql">SQL script</param>
    /// <param name="token">Token to search for</param>
    /// <returns>True if token exists within script</returns>
    public static bool ContainsToken(string sql, TSqlTokenType token)
    {
        return FindTokens(sql, [token]).Count > 0;
    }

    public static IReadOnlyList<TSqlTokenType> FindTokens(string sql, IReadOnlyList<TSqlTokenType> tokens)
    {
        if (tokens.Count == 0)
            return [];

        TSql160Parser parser = CreateParser();

        using StringReader reader = new(sql);

        IList<TSqlParserToken> stream = parser.GetTokenStream(reader, out _);

        HashSet<TSqlTokenType> wanted = [.. tokens];
        HashSet<TSqlTokenType> found = [];

        foreach (TSqlParserToken t in stream)
        {
            if (wanted.Contains(t.TokenType))
                found.Add(t.TokenType);
        }

        return [.. found];
    }

    /// <summary>
    /// Used for detecting batches within a single script
    /// 
    /// TODO: Revisit as only checking for GO, what else could be used?
    /// </summary>
    /// <param name="sql">SQL script</param>
    /// <returns>True if token(s) detected within script</returns>
    public static bool ContainsBatchToken(string sql)
    {
        return ContainsToken(sql, TSqlTokenType.Go);
    }

    /// <summary>
    /// Parses a script using a custom visitor to detect things
    /// </summary>
    /// <param name="sql">SQL to parse</param>
    /// <returns>The visitor after visiting the fragment, along with any parse errors</returns>
    private static ParseVisitResult<TVisitor> ParseVisit<TVisitor>(string sql) where TVisitor : TSqlFragmentVisitor, new()
    {
        TSql160Parser parser = CreateParser();

        using StringReader reader = new(sql);
        TSqlFragment fragment = parser.Parse(reader, out IList<ParseError> errors);

        TVisitor visitor = new();
        fragment.Accept(visitor);

        return new ParseVisitResult<TVisitor>(visitor, [.. errors]);
    }

    /// <summary>
    /// Attempts to identify the first object a script contains
    /// Used as a way to identify what object a potential batch
    /// or block in a script belongs to / modifies
    /// </summary>
    /// <param name="sql">SQL</param>
    /// <returns>The classified target object, or <c>null</c></returns>
    public static SchemaObject? GetSchemaObject(string sql)
    {
        SchemaObjectVisitor visitor = ParseVisit<SchemaObjectVisitor>(sql).Visitor;

        if (visitor.Kind is SchemaObjectKind kind && visitor.ObjectName is not null)
        {
            string objectSchema = kind == SchemaObjectKind.Schema ? string.Empty : visitor.ObjectSchema ?? "dbo";
            return new SchemaObject(objectSchema, visitor.ObjectName, kind);
        }

        if (visitor.FirstTableRef is not null)
        {
            string tableSchema = visitor.FirstTableRef.SchemaIdentifier?.Value ?? "dbo";
            return new SchemaObject(tableSchema, visitor.FirstTableRef.BaseIdentifier.Value, SchemaObjectKind.Table);
        }

        return null;
    }

    public static SchemaProgrammableDefinition? GetProgrammable(string sql)
    {
        ParseVisitResult<CreateProgrammableVisitor> result = ParseVisit<CreateProgrammableVisitor>(sql);

        if (result.Errors.Count > 0)
            return null;

        CreateProgrammableVisitor visitor = result.Visitor;

        if (visitor.Name is null || visitor.Kind is null)
            return null;

        string schema = visitor.Name.SchemaIdentifier?.Value ?? "dbo";

        SchemaObject target = new(schema, visitor.Name.BaseIdentifier.Value, visitor.Kind.Value);

        SchemaProgrammableExclusion exclusion;

        if (visitor.Indexed)
            exclusion = SchemaProgrammableExclusion.IndexedView;
        else if (visitor.SchemaBound)
            exclusion = SchemaProgrammableExclusion.SchemaBound;
        else
            exclusion = SchemaProgrammableExclusion.None;

        return new SchemaProgrammableDefinition(target, exclusion);
    }

    /// <summary>
    /// Temporary
    /// need to modify programmables from CREATE to CREATE or ALTER on the fly
    /// </summary>
    /// <param name="sql"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static string ToCreateOrAlter(string sql)
    {
        ParseVisitResult<CreateProgrammableVisitor> result = ParseVisit<CreateProgrammableVisitor>(sql);

        if (result.Errors.Count > 0)
            throw new InvalidOperationException("Cannot rewrite as CREATE OR ALTER, script contains parse errors");

        if (result.Visitor.CreateStatement is not TSqlStatement statement)
            throw new InvalidOperationException("Cannot rewrite as CREATE OR ALTER, script declares no programmable object");

        IList<TSqlParserToken> tokens = statement.ScriptTokenStream;

        for (int i = statement.FirstTokenIndex; i <= statement.LastTokenIndex; i++)
        {
            TSqlParserToken t = tokens[i];

            if (t.TokenType != TSqlTokenType.Create)
                continue;

            int offset = t.Offset + t.Text.Length;

            return string.Concat(sql.AsSpan(0, offset), " OR ALTER", sql.AsSpan(offset));
        }

        throw new InvalidOperationException("Cannot rewrite as CREATE OR ALTER, no CREATE keyword found");
    }
}
