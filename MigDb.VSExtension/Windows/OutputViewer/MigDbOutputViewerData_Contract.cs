using MigDb.VSExtension.Models;
using Microsoft.VisualStudio.Extensibility.UI;
using System.Runtime.Serialization;

namespace MigDb.VSExtension.Windows.OutputViewer;

[DataContract]
internal sealed partial class MigDbOutputViewerData
{
    [DataMember]
    public string CLIOutput
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    [DataMember]
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
                Rebuild();
        }
    } = string.Empty;

    [DataMember]
    public bool OnlyMatches
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                Rebuild();
        }
    } = true;

    [DataMember]
    public bool WrapLines
    {
        get;
        set => SetProperty(ref field, value);
    }

    [DataMember]
    public string MatchSummary
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    [DataMember]
    public bool IsSearching => SearchText.Length > 0;

    [DataMember]
    public ObservableList<OutputLine> CLIFilterLines { get; } = [];

    [DataMember]
    public IAsyncCommand ClearSearchCommand { get; }
}
