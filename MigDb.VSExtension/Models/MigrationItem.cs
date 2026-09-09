using System.Runtime.Serialization;

namespace MigDb.VSExtension.Models;

[DataContract]
internal class MigrationItem(string name, string fullName)
{
    [DataMember]
    public string Name => name;

    [DataMember]
    public string FullName => fullName;
}
