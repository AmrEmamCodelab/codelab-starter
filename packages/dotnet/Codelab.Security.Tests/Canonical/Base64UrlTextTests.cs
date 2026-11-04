using System.Security.Cryptography;
using Codelab.Security.Canonical;
using Codelab.Security.Signing;

namespace Codelab.Security.Tests.Canonical;

public sealed class Base64UrlTextTests
{
    [Fact]
    public void Encoded_bytes_decode_back_to_the_same_bytes()
    {
        var bytes = RandomNumberGenerator.GetBytes(HmacSignature.MacBytes);
        var encoded = Base64UrlText.Encode(bytes);
        Span<byte> decoded = stackalloc byte[HmacSignature.MacBytes];

        Assert.Equal(HmacSignature.MacCharacters, encoded.Length);
        Assert.True(Base64UrlText.TryDecode(encoded, decoded));
        Assert.True(decoded.SequenceEqual(bytes));
    }

    [Fact]
    public void A_spelling_with_non_zero_trailing_bits_is_refused_so_each_value_has_one_spelling()
    {
        var encoded = Base64UrlText.Encode(new byte[HmacSignature.MacBytes]);
        var sibling = encoded[..^1] + "B";

        Assert.False(Base64UrlText.TryDecode(sibling, new byte[HmacSignature.MacBytes]));
    }

    [Theory]
    [InlineData("AAAA+AAA")]
    [InlineData("AAAA/AAA")]
    [InlineData("AAAAAA==")]
    [InlineData("AAAA AAA")]
    [InlineData("AAAAٱAAA")]
    public void Characters_outside_the_unpadded_url_alphabet_are_refused_without_throwing(string value)
    {
        Assert.False(Base64UrlText.IsAlphabetOnly(value));
        Assert.False(Base64UrlText.TryDecode(value, new byte[6]));
    }

    [Fact]
    public void A_wrong_length_is_refused()
    {
        Assert.False(Base64UrlText.TryDecode("AAAA", new byte[HmacSignature.MacBytes]));
        Assert.False(Base64UrlText.TryDecode("A", out byte[] _));
    }

    [Fact]
    public void Every_single_character_mutation_of_a_mac_returns_a_verdict_and_never_throws()
    {
        var mac = Base64UrlText.Encode(RandomNumberGenerator.GetBytes(HmacSignature.MacBytes));
        foreach (var index in Enumerable.Range(0, mac.Length))
        {
            foreach (var replacement in "AB_-z9+/=")
            {
                var mutated = mac[..index] + replacement + mac[(index + 1)..];
                _ = Base64UrlText.TryDecode(mutated, new byte[HmacSignature.MacBytes]);
            }
        }
    }
}
