using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Dac;

namespace MigDb.Core.Schema;

public sealed class SchemaPublisher(ILogger<SchemaPublisher> logger, SQLConnectionFactory sqlFactory, DatabaseOptions dbOptions)
{
    public Task<string> GenerateDeployScriptAsync(string dacpacPath, SchemaDeployScope scope = SchemaDeployScope.All, CancellationToken ct = default)
    {
        return Task.Run(() => GenerateDeployScript(dacpacPath, scope, ct), ct);
    }

    public string GenerateDeployScript(string dacpacPath, SchemaDeployScope scope = SchemaDeployScope.All, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dacpacPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlFactory.ConnectionString);

        string database = new SqlConnectionStringBuilder(sqlFactory.ConnectionString).InitialCatalog;

        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("Connection string does not name a database - nothing to target");

        logger.LogDebug("Generating {Scope} deploy script from {Dacpac} for {Database}", scope, dacpacPath, database);

        ObjectType[] excludedObjects = SchemaObjectTypes.ExcludedFor(scope);

        DacDeployOptions options = new()
        {
            CreateNewDatabase = false,
            DropObjectsNotInSource = false,
            IncludeCompositeObjects = true,
            ScriptDatabaseOptions = false,
            AllowIncompatiblePlatform = true,
            IncludeTransactionalScripts = false,
            RegisterDataTierApplication = false,
            BlockOnPossibleDataLoss = true,
            ScriptRefreshModule = false,
            CommandTimeout = dbOptions.CommandTimeoutSeconds,
            ExcludeObjectTypes = excludedObjects,
        };

        using DacPackage package = DacPackage.Load(dacpacPath, DacSchemaModelStorageType.Memory);

        DacServices services = new(sqlFactory.ConnectionString);

        services.Message += OnPublishMessage;

        try
        {
            string? script = services.GenerateDeployScript(package, database, options, ct);

            logger.LogDebug("Generated deploy script ({Length} chars)", script?.Length ?? 0);

            return script ?? string.Empty;
        }
        finally
        {
            services.Message -= OnPublishMessage;
        }
    }

    private void OnPublishMessage(object? sender, DacMessageEventArgs e)
    {
        logger.LogDebug("DacFx: {Message}", e.Message);
    }
}
