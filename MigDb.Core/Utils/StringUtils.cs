namespace MigDb.Core.Utils;

public static class StringUtils
{
    /// <summary>
    /// Collapse line endings to LF, strip trailing whitespace from every line, and enforce a
    /// single trailing newline.
    /// <param name="content">Content to normalise</param>
    /// <returns>The normalised content</returns>
    public static string Normalise(string content)
    {
        string data = content.Replace("\r\n", "\n").Replace('\r', '\n');

        List<string> lines = [.. data.Split('\n').Select(x => x.TrimEnd())];

        string result = string.Join("\n", lines).TrimEnd('\n');

        if (result.Length != 0)
            result += "\n";

        return result;
    }
}
