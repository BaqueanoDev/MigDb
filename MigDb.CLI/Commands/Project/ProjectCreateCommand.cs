using MigDb.CLI.Commands.Settings;
using MigDb.CLI.Utils;
using MigDb.Core.Project;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;

namespace MigDb.CLI.Commands.Project;

internal class ProjectCreateCommand(ProjectFactory projectFactory) : AsyncCommand<ProjectCreateCommand.Settings>
{
    internal class Settings : GlobalSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Project root path (defaults to the configured schema project path)")]
        public string? Path { get; set; }

        [CommandOption("-r|--reveal")]
        [Description("Reveal directory after creating (windows only)")]
        public bool Reveal { get; set; } = false;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        SpectreUtils.WriteHeaderRule("Creating project structure");

        List<ProjectDirectory> directories;

        try
        {
            if (settings.Path is null)
                directories = projectFactory.CreateProject();
            else
                directories = projectFactory.CreateProjectAt(settings.Path);
        }
        catch (Exception ex)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]Could not create project structure: {ex.Message}[/]");
            return ExitCode.Failure;
        }

        ProjectDirectory root = directories[0];

        AnsiConsole.MarkupLineInterpolated($"Project root: [olive]{root.Info.FullName}[/]");
        SpectreUtils.WriteSectionRule();

        foreach (ProjectDirectory d in directories)
        {
            AnsiConsole.MarkupInterpolated($"Creating directory: {d.Info.Name}... ");

            if (d.Status == ProjectDirectoryStatus.Created)
                AnsiConsole.MarkupLineInterpolated($"[green]created[/]");
            else if (d.Status == ProjectDirectoryStatus.Existed)
                AnsiConsole.MarkupLineInterpolated($"[grey]exists [/]");
            else
                AnsiConsole.MarkupLineInterpolated($"[red]failed [/]");
        }

        SpectreUtils.WriteSectionRule();

        var hasFailed = directories.Any(x => x.Status == ProjectDirectoryStatus.Failed);

        if (hasFailed)
        {
            SpectreUtils.Error.MarkupLineInterpolated($"[red]One or more directories could not be created, project structure is incomplete[/]");
            return ExitCode.Failure;
        }

        if (settings.Reveal)
        {
            AnsiConsole.WriteLine("Revealing directory....");

            Process.Start(new ProcessStartInfo
            {
                FileName = root.Info.FullName,
                UseShellExecute = true
            });
        }

        return ExitCode.Success;
    }
}
