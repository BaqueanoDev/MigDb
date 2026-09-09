using Dapper;
using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.Repositories;
using MigDb.Core.Migration;
using MigDb.Core.Options;
using MigDb.Core.Schema;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Data.Common;

namespace MigDb.CLI.Commands.Schema;

internal class SchemaDeployCommand(SchemaBuilder schemaBuilder,
    SchemaPublisher schemaPublisher,
    SQLConnectionFactory sqlFactory,
    DatabaseOptions dbOptions,
    SchemaRepository schemaRepo) : AsyncCommand<SchemaDeployCommand.Settings>
{
    internal class Settings : SchemaSourceSettings, IDatabaseConnectionSetting
    {
        [CommandOption("-c|--connection <CONNECTION>")]
        [Description("Connection string to use")]
        public string? Connection { get; init; }

        [CommandOption("--scope <SCOPE>")]
        [Description("Scope to deploy: All, Schema or Programmables (default: All)")]
        public SchemaDeployScope DeployScope { get; init; } = SchemaDeployScope.All;

        [CommandOption("--platform <PLATFORM>")]
        [Description("Platform to build the schema against: Master (boxed SQL Server) or Azure (default: Azure)")]
        public SchemaSystemPlatform SystemPlatform { get; init; } = SchemaSystemPlatform.Azure;

        public override ValidationResult Validate()
        {
            ValidationResult baseResult = base.Validate();

            if (!baseResult.Successful)
                return baseResult;

            if (string.IsNullOrWhiteSpace(Connection))
                return ValidationResult.Error("--connection is required");

            return ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Deploying schema");

        AnsiConsole.MarkupLineInterpolated($"[olive]Scope:[/] [steelblue1]{settings.DeployScope}[/]");
        AnsiConsole.MarkupLineInterpolated($"[olive]System platform:[/] [steelblue1]{settings.SystemPlatform}[/]");

        if (settings.Type == SchemaSourceType.Project)
            AnsiConsole.MarkupLineInterpolated($"[olive]Project:[/] [steelblue1]{settings.Project}[/]");

        int result = await AnsiConsole.Status()
           .Spinner(Spinner.Known.BouncingBar)
           .SpinnerStyle(Style.Parse("gold1"))
           .StartAsync("[steelblue1]Deploying schema...[/]", async ctx =>
           {
               AnsiConsole.MarkupLine($"[olive]Creating work directory...[/]");

               DirectoryInfo buildDir = Directory.CreateTempSubdirectory("migdb-build");

               try
               {
                   SchemaSource source = settings.Source;

                   AnsiConsole.MarkupLineInterpolated($"[olive]Building current for {SchemaSource.ToSchemaSourceType(source)}...[/]");

                   string packPath = await schemaBuilder.BuildSourceAsync(source, buildDir.FullName, settings.SystemPlatform, ct);

                   AnsiConsole.MarkupLine($"[olive]Generating deploy script...[/]");

                   string deployScript = await schemaPublisher.GenerateDeployScriptAsync(packPath, settings.DeployScope, ct);

                   if (string.IsNullOrWhiteSpace(deployScript))
                   {
                       AnsiConsole.MarkupLine("[olive]Target already matches the schema project, nothing to deploy.[/]");
                       return ExitCode.Success;
                   }

                   AnsiConsole.MarkupLine($"[olive]Normalising script...[/]");

                   string normalisedScript = DacFxUtils.DenoiseScript(deployScript);

                   AnsiConsole.MarkupLine($"[olive]Splitting script into batches...[/]");

                   IReadOnlyList<string> batches = DacFxUtils.SplitScriptByBatch(normalisedScript);

                   SpectreUtils.WriteSectionRule();

                   await using SQLConnectionLease scope = await sqlFactory.CreateScopeAsync(null, ct);
                   await using DbTransaction transaction = await scope.Connection.BeginTransactionAsync(ct);

                   int databaseLockResult = await schemaRepo.AcquireMigrationLockAsync(transaction, ct);

                   if (databaseLockResult < 0)
                   {
                       Log.Warning("Could not acquire migration lock (result {Result}) - another migration may be in progress", databaseLockResult);
                       throw new MigrationAppLockException(databaseLockResult);
                   }

                   for (int i = 0; i < batches.Count; ++i)
                   {
                       string b = batches[i];

                       // replace line endings, and tabs so it shows up nicely in console
                       string preview = new string([.. b.ReplaceLineEndings("").Replace("\t", " ").Trim().Take(40)]) + "...";

                       CommandDefinition command = new(b, transaction: transaction, commandTimeout: dbOptions.CommandTimeoutSeconds, cancellationToken: ct);
                       await scope.Connection.ExecuteAsync(command);

                       AnsiConsole.MarkupLineInterpolated($"[olive]Applying batch ({i + 1}) out of ({batches.Count})[/] : [steelblue1]{preview}[/]");
                   }

                   SpectreUtils.WriteSectionRule();
                   AnsiConsole.MarkupLine($"[olive]Writing journaling schema...[/]");

                   await schemaRepo.EnsureSchemaExistAsync(transaction, ct);
                   await schemaRepo.EnsureSchemaTablesExistAsync(transaction, ct);

                   await transaction.CommitAsync(ct);

                   SpectreUtils.WriteSectionRule();
                   AnsiConsole.MarkupLine("[olive]Deployed. Run [/][steelblue1]schema baseline[/][olive] to record the programmable hashes.[/]");
               }
               finally
               {
                   try
                   {
                       AnsiConsole.MarkupLine($"[olive]Cleaning up...[/]");

                       buildDir.Delete(recursive: true);
                   }
                   catch (IOException ex)
                   {
                       Log.Warning(ex, "Failed to delete working directory {Dir}, will need to manually clean up (find in env tmp)", buildDir.Name);
                   }
               }

               return ExitCode.Success;
           });

        return result;
    }
}
