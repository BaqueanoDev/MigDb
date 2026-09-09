using System.Security.Cryptography;
using System.Text;

namespace MigDb.Core.Utils;

public static class HashUtils
{
    public static byte[] SHA256(string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);

        return System.Security.Cryptography.SHA256.HashData(bytes);
    }

    public static byte[] SHA256(IReadOnlyList<string> content)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (string c in content)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(c);
            hash.AppendData(bytes);
        }

        return hash.GetHashAndReset();
    }
}
