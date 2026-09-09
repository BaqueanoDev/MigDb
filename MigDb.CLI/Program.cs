using MigDb.CLI.Commands;
using MigDb.CLI.Commands.Config;
using MigDb.CLI.Commands.Database;
using MigDb.CLI.Commands.Migration;
using MigDb.CLI.Commands.Project;
using MigDb.CLI.Commands.Schema;
using MigDb.CLI.Configuration;
using MigDb.CLI.Interceptors;
using MigDb.CLI.Services;
using MigDb.CLI.Utils;
using MigDb.Core.Infrastructure.Database;
using MigDb.Core.Infrastructure.DI;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console.Cli;

namespace MigDb.CLI;

internal class Program
{
    private const string SchemaProjectPathOption = "--projectPath";

    static async Task<int> Main(string[] args)
    {
        SpectreUtils.SetColorSupport();

        // first run materialises the application config from the file sources on their own, before
        // a command line override can be written into it as though it were a stored setting
        if (!ConfigManager.ApplicationConfigExists())
            ConfigManager.WriteConfig(ConfigManager.LoadConfig());

        List<string> arguments = [.. args];

        try
        {
            ConfigManager.SchemaProjectPathOverride = TakeOptionValue(arguments, SchemaProjectPathOption);
        }
        catch (ArgumentException ex)
        {
            SpectreUtils.WriteError(ex.Message);

            return ExitCode.UsageError;
        }

        ConfigOptions config = ConfigManager.LoadConfig();

        Log.Logger = ConfigManager
            .ConfigureLogging(new LoggerConfiguration(), config.Logging)
            .CreateLogger();

        // hate this but --no-logo needs to be handled here since spectre cant
        bool skipLogo = arguments.RemoveAll(a => string.Equals(a, "--no-logo", StringComparison.OrdinalIgnoreCase)) > 0;

        if (!skipLogo)
            SpectreUtils.WriteTitle();

        try
        {
            ServiceCollection services = new();
            services.AddLogging(b => b.AddSerilog(Log.Logger, dispose: false));
            services.AddSingleton(config);
            services.AddSingleton(config.Logging);
            services.AddMigDbCore(config.Database, config.Migration);

            // i know, you might look at this and be like wtf? Spectre interceptors
            // should probably be registered using app.configure.setInterceptors.
            //
            // But since SQLConnection factory needs a logger we need to DI the factory.
            // This resulted in SQLConnectionStringInterceptor having to DI the factory too.
            // Which required interceptor to be DI which required CompositeInterceptor to be DI
            // Which required all interceptors to be DI :)

            // Thankfully Spectre gets its type from our type registrar it picks up our interceptors
            // automatically.
            services.AddSingleton<ICommandInterceptor>(sp => new CompositeInterceptor(
                new CommandInterceptor(),
                new SQLConnectionStringInterceptor(sp.GetRequiredService<SQLConnectionFactory>()))
            );

            TypeRegistrar registrar = new(services);

            CommandApp app = BuildCLIApp(registrar);

            int result = await app.RunAsync(arguments);

            return result;
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Pulls <c>--name value</c> or <c>--name=value</c> out of the argument list and hands back the
    /// value. Spectre only parses options at or after the command name, so an option that has to
    /// sit in front of it - the same problem <c>--no-logo</c> has - never reaches a settings class.
    /// </summary>
    /// <param name="arguments">Argument list, with the option removed when it is found</param>
    /// <param name="name">Option name, matched case-insensitively</param>
    /// <exception cref="ArgumentException">The option is present but has no value after it</exception>
    private static string? TakeOptionValue(List<string> arguments, string name)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];

            if (argument.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            {
                string inlineValue = argument[(name.Length + 1)..];

                if (string.IsNullOrWhiteSpace(inlineValue))
                    throw new ArgumentException($"{name} needs a path");

                arguments.RemoveAt(i);

                return inlineValue;
            }

            if (!string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                continue;

            // guards against swallowing the next option when the path was left off entirely
            if (i + 1 >= arguments.Count || arguments[i + 1].StartsWith('-'))
                throw new ArgumentException($"{name} needs a path");

            string value = arguments[i + 1];

            arguments.RemoveRange(i, 2);

            return value;
        }

        return null;
    }

    private static int HandleException(Exception ex)
    {
        if (ex is CommandAppException { Pretty: not null } pretty)
            SpectreUtils.Error.Write(pretty.Pretty);

        if (ex is CommandParseException or CommandRuntimeException)
        {
            Log.Error("Command line was rejected: {Reason}", ex.Message);
            return ExitCode.UsageError;
        }

        if (ex is CommandAppException)
        {
            Log.Error(ex, "Command is misconfigured");
            return ExitCode.Failure;
        }

        Log.Fatal(ex, "Unhandled exception");

        return ExitCode.Failure;
    }

    internal static CommandApp BuildCLIApp(ITypeRegistrar? typeRegistrar = null)
    {
        CommandApp app = new(typeRegistrar);

        app.Configure(c =>
        {
            c.Settings.StrictParsing = true;

            c.SetApplicationName("migdb");
            c.SetApplicationVersion(AssemblyUtils.GetAssemblyVersion());
            c.PropagateExceptions();

            c.AddBranch("config", a =>
            {
                a.SetDescription("Read and write the config file");

                a.AddCommand<ConfigGetCommand>("get")
                 .WithDescription("Show config values, both as stored in the targeted file and as they currently resolve")
                 .WithExample("config", "get")
                 .WithExample("config", "get", "Migration.SchemaProjectPath")
                 .WithExample("config", "get", "Database")
                 .WithExample("config", "get", "--path");

                a.AddCommand<ConfigSetCommand>("set")
                 .WithDescription("Set a config value in the user config, or in the application config with --global")
                 .WithExample("config", "set", "Migration.SchemaProjectPath", "<PATH>")
                 .WithExample("config", "set", "Logging.ConsoleLevel", "Information")
                 .WithExample("config", "set", "Database.CommandTimeoutSeconds", "1800", "--global");
            });

            c.AddBranch("project", a =>
            {
                a.SetDescription("Project utilities");

                a.AddCommand<ProjectCreateCommand>("create")
                 .WithDescription("Create the migration and schema directory structure for a project")
                 .WithExample("project", "create")
                 .WithExample("project", "create", "<PATH>")
                 .WithExample("project", "create", "<PATH>", "--reveal");
            });

            c.AddBranch("db", a =>
            {
                a.SetDescription("Database utilities");

                a.AddBranch("migration", m =>
                {
                    m.SetDescription("Database migration utilities");

                    m.AddCommand<DatabaseMigrationListCommand>("list")
                     .WithDescription("List migrations recorded in the target database")
                     .WithExample("db", "migration", "list", "-c", "<CONNECTION>");
                });

                a.AddBranch("revert", r =>
                {
                    r.SetDescription("Database revert utilities");

                    r.AddCommand<DatabaseRevertListCommand>("list")
                     .WithDescription("List what reverts have taken out of the journal in the target database")
                     .WithExample("db", "revert", "list", "-c", "<CONNECTION>")
                     .WithExample("db", "revert", "list", "-c", "<CONNECTION>", "-n", "10")
                     .WithExample("db", "revert", "list", "-c", "<CONNECTION>", "--name", "<MIGRATION NAME>");
                });
            });

            c.AddBranch("migration", a =>
            {
                a.SetDescription("Migration utilities");

                a.AddCommand<MigrationCreateCommand>("create")
                 .WithDescription("Create a migration directory")
                 .WithExample("migration", "create", "<MIGRATION NAME>")
                 .WithExample("migration", "create", "--type", "project", "--project", "<PROJECT NAME>", "<MIGRATION NAME>")
                 .WithExample("migration", "create", "<MIGRATION NAME>", "-o", "<PATH>", "--reveal");

                a.AddCommand<MigrationShowCommand>("show")
                 .WithDescription("Reveal directory in explorer (Windows only atm)")
                 .WithExample("migration", "show", "<MIGRATION NAME>")
                 .WithExample("migration", "show", "--type", "project", "--project", "<PROJECT NAME>", "<MIGRATION NAME>");

                a.AddCommand<MigrationPrepareCommand>("prepare")
                 .WithDescription("Prepare directory for migration")
                 .WithExample("migration", "prepare", "<MIGRATION NAME>")
                 .WithExample("migration", "prepare", "<PATH>", "--dry-run");

                a.AddCommand<MigrationListCommand>("list")
                 .WithDescription("List available migration directories")
                 .WithExample("migration", "list")
                 .WithExample("migration", "list", "--type", "project", "--project", "<PROJECT NAME>", "-c", "<CONNECTION>");

                a.AddCommand<MigrationHashCommand>("hash")
                 .WithDescription("Hash a migration directory, or a single sql file")
                 .WithExample("migration", "hash", "<MIGRATION NAME>")
                 .WithExample("migration", "hash", "<DIRECTORY PATH>")
                 .WithExample("migration", "hash", "<FILE PATH>");

                a.AddCommand<MigrationValidateCommand>("validate")
                 .WithDescription("Validate a directory against its manifest")
                 .WithExample("migration", "validate", "<MIGRATION NAME>")
                 .WithExample("migration", "validate", "<PATH>")
                 .WithExample("migration", "validate", "--type", "project", "--project", "<PROJECT NAME>", "<MIGRATION NAME>");

                a.AddCommand<MigrationApplyCommand>("apply")
                 .WithDescription("Apply migrations to the target database (by name/path, --pending/--tail, or --from/--to)")
                 .WithExample("migration", "apply", "<MIGRATION NAME>", "-c", "<CONNECTION>")
                 .WithExample("migration", "apply", "<PATH>", "-c", "<CONNECTION>")
                 .WithExample("migration", "apply", "--type", "project", "--project", "<PROJECT NAME>", "<MIGRATION NAME>", "-c", "<CONNECTION>")
                 .WithExample("migration", "apply", "--pending", "-c", "<CONNECTION>")
                 .WithExample("migration", "apply", "--tail", "-c", "<CONNECTION>", "--dry-run")
                 .WithExample("migration", "apply", "--from", "29/06/2026", "--to", "\"30/06/2026 13:00\"", "-c", "<CONNECTION>")
                 .WithExample("migration", "apply", "--pending", "-c", "<CONNECTION>", "--record-only");

                a.AddCommand<MigrationGenerateCommand>("generate")
                 .WithDescription("Generate delta scripts by comparing two revisions of the schema project (writes to a new timestamped directory unless -o is given)")
                 .WithExample("migration", "generate")
                 .WithExample("migration", "generate", "-o", "<MIGRATION NAME>")
                 .WithExample("migration", "generate", "-o", "<PATH>", "--source", "<REVISION>", "--target", "<REVISION>")
                 .WithExample("migration", "generate", "--scope", "Schema")
                 .WithExample("migration", "generate", "--source", "<COMMITISH>", "--target", "working")
                 .WithExample("migration", "generate", "--type", "project", "--project", "<PROJECT NAME>", "-o", "<MIGRATION NAME>");

                a.AddCommand<MigrationRevertCommand>("revert")
                 .WithDescription("Generate the scripts that undo the schema changes in the last commit (HEAD~1 vs HEAD), and with --force run them against the database and wind the journal back")
                 .WithExample("migration", "revert", "-o", "<PATH>")
                 .WithExample("migration", "revert", "-o", "<PATH>", "--target", "<COMMITISH>")
                 .WithExample("migration", "revert", "-o", "<PATH>", "-c", "<CONNECTION>")
                 .WithExample("migration", "revert", "-o", "<PATH>", "-c", "<CONNECTION>", "--force")
                 .WithExample("migration", "revert", "-o", "<PATH>", "--type", "project", "--project", "<PROJECT NAME>");
            });

            c.AddBranch("schema", a =>
            {
                a.SetDescription("Schema utilities");

                a.AddCommand<SchemaDeployCommand>("deploy")
                 .WithDescription("Deploy the schema project to the target database")
                 .WithExample("schema", "deploy", "-c", "<CONNECTION>")
                 .WithExample("schema", "deploy", "-c", "<CONNECTION>", "--scope", "Schema")
                 .WithExample("schema", "deploy", "-c", "<CONNECTION>", "--platform", "Azure")
                 .WithExample("schema", "deploy", "-c", "<CONNECTION>", "--type", "project", "--project", "<PROJECT NAME>");

                a.AddCommand<SchemaBaselineCommand>("baseline")
                 .WithDescription("Record the current programmable hashes without executing them, so the journal matches what a deploy already published")
                 .WithExample("schema", "baseline", "-c", "<CONNECTION>")
                 .WithExample("schema", "baseline", "-c", "<CONNECTION>", "--revision", "<COMMITISH>")
                 .WithExample("schema", "baseline", "-c", "<CONNECTION>", "--dry-run")
                 .WithExample("schema", "baseline", "-c", "<CONNECTION>", "--type", "project", "--project", "<PROJECT NAME>");

                a.AddCommand<SchemaDriftCommand>("drift")
                 .WithDescription("Compare the schema project against a database and report objects the two disagree on")
                 .WithExample("schema", "drift", "-c", "<CONNECTION>")
                 .WithExample("schema", "drift", "-c", "<CONNECTION>", "--platform", "Master")
                 .WithExample("schema", "drift", "-c", "<CONNECTION>", "--type", "project", "--project", "<PROJECT NAME>");

                a.AddCommand<SchemaBuildCommand>("build")
                 .WithDescription("Build the schema project and report any build errors")
                 .WithExample("schema", "build")
                 .WithExample("schema", "build", "-o", "<PATH>")
                 .WithExample("schema", "build", "--platform", "Master")
                 .WithExample("schema", "build", "--type", "project", "--project", "<PROJECT NAME>");

                a.AddBranch("journal", j =>
                {
                    j.SetDescription("Migration journal utilities");

                    j.AddCommand<SchemaJournalInitCommand>("init")
                     .WithDescription("Creates database resources")
                     .WithExample("schema", "journal", "init", "-c", "<CONNECTION>")
                     .WithExample("schema", "journal", "init", "-c", "<CONNECTION>", "--dry-run");

                    j.AddCommand<SchemaJournalStatusCommand>("status")
                     .WithDescription("Checks if required database resources exist")
                     .WithExample("schema", "journal", "status", "-c", "<CONNECTION>");

                    j.AddCommand<SchemaJournalResetCommand>("reset")
                     .WithDescription("Reset database schema")
                     .WithExample("schema", "journal", "reset", "-c", "<CONNECTION>")
                     .WithExample("schema", "journal", "reset", "-c", "<CONNECTION>", "--force");
                });
            });

            c.AddCommand<ShowLogCommand>("logs")
             .WithDescription("Reveal logs directory");
        });

        return app;
    }
}
