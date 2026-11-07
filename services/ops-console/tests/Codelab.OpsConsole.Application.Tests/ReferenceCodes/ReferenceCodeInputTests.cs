using Codelab.OpsConsole.Application.ReferenceCodes;

namespace Codelab.OpsConsole.Application.Tests.ReferenceCodes;

public sealed class ReferenceCodeInputTests
{
    [Theory]
    [InlineData("9C1E5-B7A20", "9C1E5-B7A20")]
    [InlineData("9c1e5-b7a20", "9C1E5-B7A20")]
    [InlineData("  9c1e5-B7A20\t", "9C1E5-B7A20")]
    [InlineData("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", "9C1E5-B7A20")]
    [InlineData("9C1E5B7A20D84F6E8A3B1C2D4E5F6A7B", "9C1E5-B7A20")]
    public void A_code_or_a_correlation_id_becomes_the_reference_code(string input, string expected)
    {
        Assert.True(ReferenceCodeInput.TryNormalize(input, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9C1E5B7A20")]
    [InlineData("9C1E5_B7A20")]
    [InlineData("9C1E5-B7A2")]
    [InlineData("9C1E5-B7A20-")]
    [InlineData("9C1E5-B7A2G")]
    [InlineData("9C1E5'B7A20")]
    [InlineData("9C1E5-B7A2'")]
    [InlineData("' or 1=1 --")]
    [InlineData("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7")]
    [InlineData("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7z")]
    public void Anything_else_is_refused(string? input)
    {
        Assert.False(ReferenceCodeInput.TryNormalize(input, out var code));
        Assert.Equal(string.Empty, code);
    }
}
