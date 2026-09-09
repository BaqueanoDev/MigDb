using MigDb.Core.Utils;

namespace MigDb.Test.Core;

public class StringUtilsTests
{
    [Theory]
    [InlineData("a\r\nb", "a\nb\n")]
    [InlineData("a\rb", "a\nb\n")]
    [InlineData("a\nb", "a\nb\n")]
    public void Normalise_LineEndings_CollapsedToLf(string input, string expected)
    {
        Assert.Equal(expected, StringUtils.Normalise(input));
    }

    [Theory]
    [InlineData("a   \nb\t\n", "a\nb\n")]
    [InlineData("SELECT 1;   ", "SELECT 1;\n")]
    public void Normalise_TrailingWhitespace_StrippedPerLine(string input, string expected)
    {
        Assert.Equal(expected, StringUtils.Normalise(input));
    }

    [Theory]
    [InlineData("a", "a\n")]
    [InlineData("a\n", "a\n")]
    [InlineData("a\n\n\n", "a\n")]
    public void Normalise_TrailingNewlines_CollapsedToOne(string input, string expected)
    {
        Assert.Equal(expected, StringUtils.Normalise(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("   ")]
    [InlineData("\r\n\r\n")]
    public void Normalise_NoContent_Empty(string input)
    {
        Assert.Equal(string.Empty, StringUtils.Normalise(input));
    }

    [Fact]
    public void Normalise_LeadingWhitespace_Preserved()
    {
        Assert.Equal("\tindented\n", StringUtils.Normalise("\tindented"));
    }

    [Fact]
    public void Normalise_InteriorBlankLines_Preserved()
    {
        Assert.Equal("a\n\nb\n", StringUtils.Normalise("a\r\n\r\nb\r\n"));
    }

    [Fact]
    public void Normalise_Idempotent()
    {
        string once = StringUtils.Normalise("a   \r\nb\r\n\r\n");

        Assert.Equal(once, StringUtils.Normalise(once));
    }
}
