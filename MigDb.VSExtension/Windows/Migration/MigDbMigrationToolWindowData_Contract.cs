using MigDb.VSExtension.Models;
using Microsoft.VisualStudio.Extensibility.UI;
using System.Runtime.Serialization;

namespace MigDb.VSExtension.Windows;

[DataContract]
internal partial class MigDbMigrationToolWindowData : NotifyPropertyChangedObject
{
    [DataMember]
    public ObservableList<MigrationItem> Migrations { get; } = [];

    [DataMember]
    public ObservableList<ProjectTab> Projects { get; } = [];

    [DataMember]
    public bool IsConfigured
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                SetCommandStates();
        }
    }

    [DataMember]
    public bool IsProjectTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    }

    [DataMember]
    public ProjectTab? SelectedProject
    {
        get;
        set => SetProperty(ref field, value);
    }

    [DataMember]
    public MigrationItem? SelectedMigration
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value is not null)
                ActiveDirectory = value.FullName;
        }
    }

    [DataMember]
    public string ActiveDirectory
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                SetCommandStates();
        }
    } = string.Empty;

    // Owned by OutputBufferService so the MigDb Output tool window shows the same text.
    [DataMember]
    public string Status
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    [DataMember]
    public bool HasValidSelection => !string.IsNullOrWhiteSpace(ActiveDirectory) && Directory.Exists(ActiveDirectory);

    [DataMember]
    public bool CanWorkOnDirectory => IsConfigured && HasValidSelection;

    [DataMember]
    public IAsyncCommand BrowseCommand { get; }

    [DataMember]
    public IAsyncCommand RevealCommand { get; }

    [DataMember]
    public IAsyncCommand CreateMigrationCommand { get; }

    [DataMember]
    public IAsyncCommand GenerateCommand { get; }

    [DataMember]
    public IAsyncCommand BuildCommand { get; }

    [DataMember]
    public IAsyncCommand PrepareCommand { get; }

    [DataMember]
    public IAsyncCommand ValidateCommand { get; }

    [DataMember]
    public IAsyncCommand RefreshCommand { get; }

    [DataMember]
    public IAsyncCommand ExpandOutputCommand { get; }
}
