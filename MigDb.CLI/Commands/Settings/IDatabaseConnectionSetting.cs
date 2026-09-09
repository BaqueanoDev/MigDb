namespace MigDb.CLI.Commands.Settings;

// Hate this, only way to merge different settings
// so that interceptor can pick up required or optional connection string
internal interface IDatabaseConnectionSetting
{
    public string? Connection { get; }
}
