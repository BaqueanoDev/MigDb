using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration;
using MigDb.Core.Migration.Manifest;
using MigDb.Core.Migration.Revert;
using MigDb.Core.Migration.Run;
using MigDb.Core.Migration.Run.Step;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Options;
using MigDb.Core.Project;
using MigDb.Core.Schema;
using MigDb.Core.Schema.Programmable;
using Microsoft.Extensions.DependencyInjection;

namespace MigDb.Core.Infrastructure.DI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// This is a helper extension to help with registering the minimum amount of 
    /// services required to run a project
    /// 
    /// This is me toying with DI and simplifying future projects
    /// </summary>
    /// <param name="services">Service Collection</param>
    /// <param name="databaseOptions">Database opions</param>
    /// <param name="migrationOptions">Migration options</param>
    /// <returns>Service collection passed in</returns>
    public static IServiceCollection AddMigDbCore(
        this IServiceCollection services,
        DatabaseOptions databaseOptions,
        MigrationOptions migrationOptions)
    {
        services.AddSingleton(databaseOptions);
        services.AddSingleton(migrationOptions);

        services.AddSchemaServices();
        services.AddMigrationServices();

        services.AddSingleton<SQLConnectionFactory>();
        services.AddSingleton<ProjectFactory>();

        return services;
    }

    public static IServiceCollection AddSchemaServices(this IServiceCollection services)
    {
        services.AddSingleton<SchemaRepository>();
        services.AddSingleton<SchemaProgrammableRepository>();
        services.AddSingleton<SchemaPathResolver>();
        services.AddSingleton<SchemaProgrammableLoader>();
        services.AddSingleton<SchemaProgrammableScanner>();
        services.AddSingleton<SchemaProgrammableRunner>();
        services.AddSingleton<SchemaComparer>();
        services.AddSingleton<SchemaBuilder>();
        services.AddSingleton<SchemaPublisher>();

        return services;
    }

    public static IServiceCollection AddMigrationServices(this IServiceCollection services)
    {
        services.AddSingleton<MigrationRunRepository>();
        services.AddSingleton<MigrationDirectoryRepository>();
        services.AddSingleton<MigrationFileRepository>();
        services.AddSingleton<MigrationProgrammableRepository>();
        services.AddSingleton<MigrationRevertRepository>();
        services.AddSingleton<MigrationPathResolver>();
        services.AddSingleton<MigrationPlanner>();
        services.AddSingleton<MigrationHasher>();
        services.AddSingleton<MigrationManifestStore>();
        services.AddSingleton<MigrationValidator>();
        services.AddSingleton<MigrationLoader>();
        services.AddSingleton<MigrationDirectoryFactory>();
        services.AddSingleton<MigrationStepFactory>();
        services.AddSingleton<MigrationSelector>();
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<MigrationGenerator>();
        services.AddSingleton<MigrationJournalReverter>();

        return services;
    }
}