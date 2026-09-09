namespace MigDb.Test.Utils;

internal sealed class TestDirectory : IDisposable
{
    public DirectoryInfo Info { get; }

    public TestDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"migdb_test_{Guid.NewGuid():N}");
        Info = Directory.CreateDirectory(path);
    }

    public string WriteFile(string name, string content = "")
    {
        string full = Path.Combine(Info.FullName, name);
        File.WriteAllText(full, content);
        return full;
    }

    public DirectoryInfo CreateSubDirectory(string name)
    {
        DirectoryInfo info = Directory.CreateDirectory(Path.Combine(Info.FullName, name));
        return info;
    }

    public void Dispose()
    {
        try
        {
            if (Info.Exists)
                Info.Delete(recursive: true);
        }
        catch (IOException)
        {

        }
    }
}
