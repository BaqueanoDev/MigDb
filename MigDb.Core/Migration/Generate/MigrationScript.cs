using MigDb.Core.Schema;

namespace MigDb.Core.Migration.Generate;

/// <summary>
/// One generated script destined for a single migration file.
/// </summary>
/// <param name="Object">The object the script targets</param>
/// <param name="Script">The script, holding exactly one batch</param>
/// <param name="Warnings">DacFx warnings attributed to the object</param>
/// <param name="Sequence">1-based position in the generated set, in dependency order</param>
public sealed record MigrationScript(SchemaObject Object, string Script, IReadOnlyList<string> Warnings, int Sequence)
{
    /// <summary>
    /// File name to write the script as.
    ///
    /// The numeric prefix pins the order the scripts were generated in, which is the order they
    /// have to run in. 'migration prepare' leaves an already numbered file alone
    /// (<see cref="MigrationPlanner"/>), so the order survives into the applied migration - and it
    /// keeps the names apart when one object spans several batches.
    /// </summary>
    public string FileName => $"{Sequence:D2}_{Object.Kind}_{Object.FullName}.sql";
}
