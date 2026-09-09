using Microsoft.VisualStudio.Extensibility.UI;
using System.Runtime.Serialization;

namespace MigDb.VSExtension.Models;


[DataContract]
internal sealed class OutputLine(int number, IEnumerable<OutputSegment> segments)
{
    [DataMember]
    public int Number => number;

    [DataMember]
    public ObservableList<OutputSegment> Segments { get; } = [.. segments];
}
