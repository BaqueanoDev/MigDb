using MigDb.Core.Migration;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Options;
using MigDb.Core.Schema.Programmable;
using Microsoft.Extensions.Logging;

namespace MigDb.Core.Migration.Run.Step;

public sealed class MigrationStepFactory(
    ILoggerFactory loggerFactory,
    MigrationValidator validator,
    MigrationHasher hasher,
    MigrationDirectoryRepository directoryRepo,
    MigrationFileRepository fileRepo,
    SchemaProgrammableRunner programmableRunner,
    MigrationProgrammableRepository migrationProgrammableRepo,
    DatabaseOptions dbOptions,
    SQLConnectionFactory sqlFactory)
{

    public IMigrationStep MigrationStep(IReadOnlyList<MigrationDirectory> migrations, MigrationRunMode mode = MigrationRunMode.Apply)
    {
        return new ApplyMigrationDirectoryStep(migrations, mode, loggerFactory.CreateLogger<ApplyMigrationDirectoryStep>(), hasher, directoryRepo, fileRepo, dbOptions, sqlFactory);
    }

    public IMigrationValidationStep MigrationValidationStep(IReadOnlyList<MigrationDirectory> migrations)
    {
        return new ValidateMigrationDirectoryStep(migrations, loggerFactory.CreateLogger<ValidateMigrationDirectoryStep>(), validator);
    }

    public IMigrationStep ProgrammableStep(IReadOnlyList<SchemaProgrammableFile> programmables, MigrationRunMode mode = MigrationRunMode.Apply)
    {
        return new ApplyProgrammableStep(programmables, mode, loggerFactory.CreateLogger<ApplyProgrammableStep>(), programmableRunner, migrationProgrammableRepo);
    }

    public IMigrationValidationStep ProgrammableValidationStep(IReadOnlyList<SchemaProgrammableFile> programmables)
    {
        return new ValidateProgrammableStep(programmables, loggerFactory.CreateLogger<ValidateProgrammableStep>(), validator);
    }
}
