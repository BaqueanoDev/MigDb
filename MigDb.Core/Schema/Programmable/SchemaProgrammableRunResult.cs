namespace MigDb.Core.Schema.Programmable;

public sealed record SchemaProgrammableRunResult(IReadOnlyList<SchemaProgrammableDeploy> Deploys, int Created, int Updated, int Skipped, int RunDuration)
{
    public IReadOnlyList<SchemaProgrammableFile> Excluded { get; init; } = [];

    public int DeployedCount => Created + Updated;

    public int TotalCount => Created + Updated + Skipped + Excluded.Count;
}
