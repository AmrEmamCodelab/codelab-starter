namespace Codelab.Observability.Tests;

/// <summary>The helpdesk reference code: first 10 hex characters, uppercase, grouped five and five.</summary>
public sealed class ReferenceCodeTests
{
    [Fact]
    public void Is_first_ten_hex_uppercase_grouped_five_five()
    {
        Assert.Equal("9C1E5-B7A20", ReferenceCode.From("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b"));
    }

    [Fact]
    public void Uppercase_input_gives_the_same_code()
    {
        Assert.Equal(
            ReferenceCode.From("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b"),
            ReferenceCode.From("9C1E5B7A20D84F6E8A3B1C2D4E5F6A7B"));
    }

    [Theory]
    [InlineData("abcdef0123456789")]
    [InlineData("9c1e5b7a-20d8-4f6e-8a3b-1c2d4e5f6a7b")]
    [InlineData("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7z")]
    [InlineData("")]
    public void An_id_this_host_did_not_mint_is_returned_unchanged(string correlationId)
    {
        Assert.Same(correlationId, ReferenceCode.From(correlationId));
    }

    [Fact]
    public void Null_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => ReferenceCode.From(null!));
    }
}
