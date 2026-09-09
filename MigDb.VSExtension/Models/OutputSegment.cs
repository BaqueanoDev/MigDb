using Microsoft.VisualStudio.Extensibility.UI;
using System.Runtime.Serialization;

namespace MigDb.VSExtension.Models;

[DataContract]
internal sealed class OutputSegment(string text, bool isMatch) : NotifyPropertyChangedObject
{
    [DataMember]
    public string Text => text;

    [DataMember]
    public bool IsMatch => isMatch;

    [DataMember]
    public bool IsCurrent
    {
        get;
        set => SetProperty(ref field, value);
    }
}
