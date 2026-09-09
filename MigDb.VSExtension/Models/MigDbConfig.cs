namespace MigDb.VSExtension.Models;

internal sealed record MigDbConfig
{
    public MigrationConfig Migration { get; init; } = new MigrationConfig();
}

internal sealed record MigrationConfig
{
    public string? SchemaProjectPath { get; init; }
}
