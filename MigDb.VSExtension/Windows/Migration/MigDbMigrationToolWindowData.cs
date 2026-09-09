using MigDb.VSExtension.Models;
using MigDb.VSExtension.Services;
using MigDb.VSExtension.Windows.OutputViewer;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell.FileDialog;
using Microsoft.VisualStudio.Extensibility.UI;
using System.Diagnostics;

namespace MigDb.VSExtension.Windows;

internal partial class MigDbMigrationToolWindowData
{
    private readonly VisualStudioExtensibility _extensibility;
    private readonly MigDbCLIService _cliService;
    private readonly OutputBufferService _outputBuffer;

    public MigDbMigrationToolWindowData(VisualStudioExtensibility extensibility, MigDbCLIService cliService, OutputBufferService outputBuffer)
    {
        _cliService = cliService;
        _extensibility = extensibility;
        _outputBuffer = outputBuffer;

        Status = _outputBuffer.Text;
        _outputBuffer.TextChanged += OnOutputTextChanged;

        BrowseCommand = new AsyncCommand(async (parameter, clientContext, ct) =>
        {
            string initialDirectory = string.Empty;

            if (HasValidSelection)
            {
                initialDirectory = ActiveDirectory;
            }
            else
            {
                string? commonPath = await cliService.GetCommonMigrationsPathAsync(ct);

                if (commonPath is not null && Directory.Exists(commonPath))
                    initialDirectory = commonPath;
            }

            FolderDialogOptions options = new()
            {
                Title = "Select migration directory",
                InitialDirectory = initialDirectory,
            };

            string? selected = await _extensibility.Shell().ShowOpenFolderDialogAsync(options, ct);

            if (!string.IsNullOrWhiteSpace(selected))
                ActiveDirectory = selected;
        });

        RevealCommand = CreateGuardedCommand("Reveal", ct =>
        {
            if (!HasValidSelection)
                return Task.CompletedTask;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/n,\"{ActiveDirectory}\"",
                UseShellExecute = true,
            });

            return Task.CompletedTask;
        });

        CreateMigrationCommand = CreateGuardedCommand("Create migration", CreateMigrationDirectoryAsync);

        GenerateCommand = CreateGuardedCommand("Generate", async ct =>
        {
            if (!HasValidSelection && await CreateMigrationDirectoryAsync(ct) is null)
                return;

            await RunCliAsync($"migration generate -o \"{ActiveDirectory}\"", ct);
        });

        BuildCommand = CreateGuardedCommand("Build", async ct =>
        {
            await RunCliAsync("schema build", ct);
        });

        PrepareCommand = CreateGuardedCommand("Prepare", async ct =>
        {
            if (!HasValidSelection)
                return;

            await RunCliAsync($"migration prepare \"{ActiveDirectory}\"", ct);
        });

        ValidateCommand = CreateGuardedCommand("Validate", async ct =>
        {
            if (!HasValidSelection)
                return;

            await RunCliAsync($"migration validate \"{ActiveDirectory}\"", ct);
        });

        RefreshCommand = CreateGuardedCommand("Refresh", async ct =>
        {
            SetCLISetupState();

            if (!_cliService.IsSetup)
                return;

            await LoadProjectListAsync(ct);
            await LoadMigrationDirectoryListAsync(ct);

            SetCommandStates();

            if (!string.IsNullOrWhiteSpace(ActiveDirectory) && !HasValidSelection)
            {
                _outputBuffer.Set($"Directory no longer exists:\n\n{ActiveDirectory}");
                ActiveDirectory = string.Empty;
            }
        });

        ExpandOutputCommand = CreateGuardedCommand("Open output", async ct =>
        {
            await _extensibility.Shell().ShowToolWindowAsync<MigDbOutputViewerDataToolWindow>(activate: true, ct);
        });

        SetCommandStates();
    }

    private void OnOutputTextChanged(string text)
    {
        Status = text;
    }

    private async Task<MigrationItem?> CreateMigrationDirectoryAsync(CancellationToken cancellationToken)
    {
        if (IsProjectTabSelected && SelectedProject is null)
        {
            _outputBuffer.Set("Select a project tab before creating a project migration.");
            return null;
        }

        string arguments = "migration create";

        if (IsProjectTabSelected)
            arguments += $" -t project --project \"{SelectedProject!.Name}\"";

        ObservableList<MigrationItem> migrations = IsProjectTabSelected ? SelectedProject!.Migrations : Migrations;

        HashSet<string> existing = migrations.Select(m => m.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        CLIRunResult result = await RunCliAsync(arguments, cancellationToken);

        if (result.ExitCode != 0)
            return null;

        if (!IsProjectTabSelected)
            await LoadMigrationDirectoryListAsync(cancellationToken);
        else
            await LoadProjectMigrationsAsync(SelectedProject!, cancellationToken);

        MigrationItem? created = migrations.FirstOrDefault(m => !existing.Contains(m.FullName));

        if (created is null)
            return null;

        SelectedMigration = created;

        return created;
    }

    private async Task<CLIRunResult> RunCliAsync(string arguments, CancellationToken cancellationToken)
    {
        _outputBuffer.Restart($"MigDb.exe {arguments}");

        CLIRunResult result = await _cliService.RunCommandAsync(arguments, cancellationToken, _outputBuffer.AppendLine);

        _outputBuffer.AppendLine($"{Environment.NewLine}Exited with code {result.ExitCode}");

        return result;
    }

    private AsyncCommand CreateGuardedCommand(string operation, Func<CancellationToken, Task> action)
    {
        AsyncCommand command = new(async (parameter, clientContext, cancellationToken) =>
        {
            await RunCommandAsync(operation, action, cancellationToken);
        });

        return command;
    }

    private async Task RunCommandAsync(string operation, Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException)
        {
            // Tool window closed or the command was cancelled — nothing worth reporting.
        }
        catch (Exception exception)
        {
            _outputBuffer.Set($"{operation} failed.\n\n{exception}");
        }
    }

    public void SetCLISetupState()
    {
        IsConfigured = _cliService.IsSetup;

        if (IsConfigured)
            return;

        List<string> messages = [];

        if (!_cliService.IsMigDbCliInPath())
            messages.Add($"{MigDbCLIService.CLIAssemblyName} is not in PATH");

        if (!MigDbCLIService.ConfigExists)
            messages.Add($"Missing config file at {MigDbCLIService.ConfigFilePath}");

        string details = string.Join("\n", messages);
        string message = $"Setup or config issue: {details}.";

        _outputBuffer.Set($"{message}\n\nFix it, then click Refresh.");
    }

    private void SetCommandStates()
    {
        RaiseNotifyPropertyChangedEvent(nameof(HasValidSelection));
        RaiseNotifyPropertyChangedEvent(nameof(CanWorkOnDirectory));

        if (BrowseCommand is AsyncCommand browseCommand)
            browseCommand.CanExecute = IsConfigured;

        if (RevealCommand is AsyncCommand revealCommand)
            revealCommand.CanExecute = CanWorkOnDirectory;

        if (CreateMigrationCommand is AsyncCommand createCommand)
            createCommand.CanExecute = IsConfigured;

        if (GenerateCommand is AsyncCommand generateCommand)
            generateCommand.CanExecute = IsConfigured;

        if (BuildCommand is AsyncCommand buildCommand)
            buildCommand.CanExecute = IsConfigured;

        if (ValidateCommand is AsyncCommand validateCommand)
            validateCommand.CanExecute = CanWorkOnDirectory;

        if (PrepareCommand is AsyncCommand prepareCommand)
            prepareCommand.CanExecute = CanWorkOnDirectory;
    }

    public async Task LoadMigrationDirectoryListAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return;

        IReadOnlyList<MigrationItem> migrations = await _cliService.ListCommonMigrationsAsync(cancellationToken);

        List<MigrationItem> sortedMigrations = migrations.Reverse().ToList();

        SyncMigrationsList(Migrations, sortedMigrations);
    }

    public async Task LoadProjectListAsync(CancellationToken cancellationToken)
    {
        if (!MigDbCLIService.ConfigExists)
            return;

        string? previous = SelectedProject?.Name;

        IReadOnlyList<string> projects = await _cliService.ListProjectsAsync(cancellationToken);

        Projects.Clear();

        foreach (string project in projects)
        {
            ProjectTab tab = new(project);

            await LoadProjectMigrationsAsync(tab, cancellationToken);

            Projects.Add(tab);
        }

        SelectedProject = Projects.FirstOrDefault(c => string.Equals(c.Name, previous, StringComparison.OrdinalIgnoreCase)) ?? Projects.FirstOrDefault();
    }

    public async Task LoadProjectMigrationsAsync(ProjectTab project, CancellationToken cancellationToken)
    {
        IReadOnlyList<MigrationItem> migrations = await _cliService.ListProjectMigrationsAsync(project.Name, cancellationToken);

        List<MigrationItem> sortedMigrations = [.. migrations.Reverse()];

        SyncMigrationsList(project.Migrations, sortedMigrations);
    }

    // this is actually gross, since the UI drops the full list every time we have
    // to manually merge either the lists so it remains active and doesnt drop selection
    private static void SyncMigrationsList(ObservableList<MigrationItem> target, List<MigrationItem> source)
    {
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!source.Any(m => string.Equals(m.FullName, target[i].FullName, StringComparison.OrdinalIgnoreCase)))
                target.RemoveAt(i);
        }

        for (int i = 0; i < source.Count; i++)
        {
            if (i >= target.Count)
                target.Add(source[i]);
            else if (!string.Equals(source[i].FullName, target[i].FullName, StringComparison.OrdinalIgnoreCase))
                target.Insert(i, source[i]);
        }
    }
}
