using System.Security.Cryptography;
using System.Text;
using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;

namespace Codelab.Identity.Tests.Oidc;

public sealed class PkceAndMatchTests
{
    private const string UnreservedCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    [Fact]
    public void A_verifier_is_43_unreserved_characters()
    {
        var verifier = Pkce.NewVerifier();

        Assert.Equal(43, verifier.Length);
        Assert.All(verifier, character => Assert.Contains(character, UnreservedCharacters));
    }

    [Fact]
    public void The_challenge_is_base64url_of_sha256_without_padding()
    {
        var verifier = Pkce.NewVerifier();

        // Recomputed through a different path: standard base64, then the RFC 4648 section 5 alphabet by hand.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(verifier));
        var expected = Convert.ToBase64String(hash.GetHashAndReset()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(expected, Pkce.ChallengeOf(verifier));
        Assert.Equal(43, expected.Length);
    }

    [Fact]
    public void Opaque_values_are_never_repeated()
    {
        var values = Enumerable.Range(0, 200).Select(_ => Pkce.NewOpaqueValue()).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(200, values.Count);
    }

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", "abcd", false)]
    [InlineData(null, "abc", false)]
    [InlineData(null, null, false)]
    public void Single_use_values_compare_exactly(string? left, string? right, bool equal) =>
        Assert.Equal(equal, Pkce.FixedTimeEquals(left, right));

    [Theory]
    [InlineData("/", true)]
    [InlineData("/admin/users?page=2", true)]
    [InlineData("//evil.example.org", false)]
    [InlineData("/\\evil.example.org", false)]
    [InlineData("https://evil.example.org", false)]
    [InlineData("/x?u=https://evil.example.org", false)]
    [InlineData("relative", false)]
    [InlineData("/line\nbreak", false)]
    [InlineData(null, false)]
    public void Only_root_relative_paths_are_return_urls(string? value, bool allowed) =>
        Assert.Equal(allowed, ReturnUrls.IsRootRelativePath(value));

    [Theory]
    [InlineData(LoginMatchField.Email, "mail@example.org")]
    [InlineData(LoginMatchField.LoginName, "user.one")]
    [InlineData(LoginMatchField.Upn, "user.one@example.org")]
    public void The_match_value_comes_from_the_configured_field(LoginMatchField field, string expected)
    {
        var directory = new DirectoryIdentity("user.one", " user.one@example.org ", "mail@example.org", "User One");
        var oidc = new OidcIdentity("sub", "mail@example.org", "user.one", "user.one@example.org", "User One", "token");

        Assert.Equal(expected, LoginMatch.ValueOf(directory, field));
        Assert.Equal(expected, LoginMatch.ValueOf(oidc, field));
    }

    [Fact]
    public void An_absent_or_blank_match_value_is_null()
    {
        var oidc = new OidcIdentity("sub", Email: null, LoginName: " ", Upn: null, DisplayName: null, IdToken: "token");

        Assert.Null(LoginMatch.ValueOf(oidc, LoginMatchField.Email));
        Assert.Null(LoginMatch.ValueOf(oidc, LoginMatchField.LoginName));
    }

    [Fact]
    public void An_undefined_match_field_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LoginMatch.ValueOf(new DirectoryIdentity("a", null, null, null), (LoginMatchField)0));

    [Fact]
    public void Only_asymmetric_algorithms_are_accepted()
    {
        Assert.True(OidcAlgorithms.IsAsymmetric("RS256"));
        Assert.True(OidcAlgorithms.IsAsymmetric("ES256"));
        Assert.False(OidcAlgorithms.IsAsymmetric("none"));
        Assert.False(OidcAlgorithms.IsAsymmetric("HS256"));
        Assert.False(OidcAlgorithms.IsAsymmetric("rs256"));
        Assert.Equal(["RS256"], new OidcOptions().EffectiveAllowedAlgorithms);
    }
}
