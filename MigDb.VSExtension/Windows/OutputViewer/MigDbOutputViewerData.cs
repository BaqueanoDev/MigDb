using MigDb.VSExtension.Models;
using MigDb.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility.UI;

namespace MigDb.VSExtension.Windows.OutputViewer;

internal sealed partial class MigDbOutputViewerData : NotifyPropertyChangedObject
{
    private List<OutputSegment> Matches { get; set; } = [];

    public MigDbOutputViewerData(OutputBufferService outputBuffer)
    {
        CLIOutput = outputBuffer.Text;
        outputBuffer.TextChanged += OnOutputTextChanged;

        // this is gross
        ClearSearchCommand = new AsyncCommand((_, _, _) =>
        {
            SearchText = string.Empty;
            return Task.CompletedTask;
        })
        {
            CanExecute = false
        };
    }

    private void OnOutputTextChanged(string text)
    {
        CLIOutput = text;

        if (IsSearching)
            Rebuild();
    }

    private void Rebuild()
    {
        Matches.Clear();
        CLIFilterLines.Clear();

        RaiseNotifyPropertyChangedEvent(nameof(IsSearching));

        if (!IsSearching)
        {
            MatchSummary = string.Empty;
            SetCommandStates();
            return;
        }

        string[] allLines = CLIOutput.Split('\n');
        int matchingLines = 0;

        for (int index = 0; index < allLines.Length; index++)
        {
            List<OutputSegment> segments = SplitLine(allLines[index].TrimEnd('\r'), SearchText);
            List<OutputSegment> lineMatches = [.. segments.Where(segment => segment.IsMatch)];

            if (lineMatches.Count > 0)
                matchingLines++;
            else if (OnlyMatches)
                continue;

            CLIFilterLines.Add(new OutputLine(index + 1, segments));
            Matches.AddRange(lineMatches);
        }

        MatchSummary = BuildMatchSummary(matchingLines);
        SetCommandStates();
    }

    private string BuildMatchSummary(int matchingLines)
    {
        if (Matches.Count == 0)
            return "No matches";

        string summary = $"{Matches.Count} in {matchingLines} line{(matchingLines == 1 ? string.Empty : "s")}";

        return summary;
    }

    private static List<OutputSegment> SplitLine(string line, string search)
    {
        List<OutputSegment> segments = [];
        int position = 0;

        while (position < line.Length)
        {
            int hit = line.IndexOf(search, position, StringComparison.OrdinalIgnoreCase);

            if (hit < 0)
                break;

            if (hit > position)
                segments.Add(new OutputSegment(line[position..hit], isMatch: false));

            segments.Add(new OutputSegment(line.Substring(hit, search.Length), isMatch: true));
            position = hit + search.Length;
        }

        if (position < line.Length)
            segments.Add(new OutputSegment(line[position..], isMatch: false));

        if (segments.Count == 0)
            segments.Add(new OutputSegment(" ", isMatch: false));

        return segments;
    }

    private void SetCommandStates()
    {
        if (ClearSearchCommand is AsyncCommand clear)
            clear.CanExecute = IsSearching;
    }
}
