using MigDb.Core.Utils;

namespace MigDb.Test.Core;

public class HashUtilsTests
{
    // pinned so a change to hashing is a deliberate act: migration hashes are persisted, and
    // a silent change would make every applied migration look tampered with
    [Theory]
    [InlineData("", "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("test", "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")]
    public void Sha256_KnownInput_ExpectedHash(string content, string expected)
    {
        Assert.Equal(expected, Convert.ToHexString(HashUtils.SHA256(content)));
    }

    [Fact]
    public void Sha256_Sequence_MatchesConcatenation()
    {
        Assert.Equal(HashUtils.SHA256("ab"), HashUtils.SHA256(["a", "b"]));
    }

    [Fact]
    public void Sha256_Sequence_OrderIsSignificant()
    {
        Assert.NotEqual(HashUtils.SHA256(["a", "b"]), HashUtils.SHA256(["b", "a"]));
    }

    [Fact]
    public void Sha256_Sequence_Empty_MatchesEmptyString()
    {
        Assert.Equal(HashUtils.SHA256(string.Empty), HashUtils.SHA256([]));
    }

    [Fact]
    public void Sha256_Deterministic()
    {
        Assert.Equal(HashUtils.SHA256("SELECT 1;"), HashUtils.SHA256("SELECT 1;"));
    }
}
