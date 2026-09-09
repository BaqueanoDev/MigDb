namespace MigDb.Core.Schema;

/// <summary>
/// A single schema object's generated migration: the object it targets, the
/// merged DDL/warning script, and the DacFx warnings attributed to it.
/// </summary>
/// <param name="Object">The target object and its kind</param>
/// <param name="Script">Merged script (warnings floated to the top)</param>
/// <param name="Warnings">Warnings attributed to the object</param>
public sealed record SchemaObjectScript(SchemaObject Object, string Script, List<string> Warnings);
