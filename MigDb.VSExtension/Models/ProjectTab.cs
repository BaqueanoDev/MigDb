using Microsoft.VisualStudio.Extensibility.UI;
using System.Runtime.Serialization;

namespace MigDb.VSExtension.Models;

[DataContract]
internal class ProjectTab(string name)
{
    [DataMember]
    public string Name => name;

    [DataMember]
    public ObservableList<MigrationItem> Migrations { get; } = [];
}
