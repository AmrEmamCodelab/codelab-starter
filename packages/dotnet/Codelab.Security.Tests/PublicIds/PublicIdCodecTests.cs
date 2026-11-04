using Codelab.Security.PublicIds;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.PublicIds;

public sealed class PublicIdCodecTests
{
    private const string Domain = "record";
    private static readonly PublicIdKeyringOptions RingOptions = TestKeys.Ring(new PublicIdKeyringOptions(), "k1");
    private readonly PublicIdCodec _codec = new(PublicIdKeyring.Create(RingOptions, TestProtocols.Test), TestProtocols.Test);

    [Fact]
    public void A_token_has_the_published_shape_and_round_trips()
    {
        var token = _codec.Protect(Domain, 42);

        Assert.Matches(PublicIdFormat.OpenApiPattern, token);
        Assert.StartsWith("m1_k1_", token, StringComparison.Ordinal);
        Assert.True(_codec.TryUnprotect(Domain, token, out var id));
        Assert.Equal(42, id);
    }

    [Fact]
    public void The_same_id_yields_the_same_token_and_different_ids_never_collide()
    {
        Assert.Equal(_codec.Protect(Domain, 7), _codec.Protect(Domain, 7));
        var tokens = Enumerable.Range(1, 500).Select(id => _codec.Protect(Domain, id)).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token => Assert.Equal(Uri.EscapeDataString(token), token));
    }

    [Fact]
    public void A_token_minted_under_an_old_key_still_reads_after_rotation()
    {
        var old = _codec.Protect(Domain, 9);
        var rotated = new PublicIdKeyringOptions { ActiveKeyId = "k2" };
        rotated.Keys.Add(RingOptions.Keys[0]);
        rotated.Keys.Add(new Codelab.Security.Keys.KeyEntryOptions { KeyId = "k2", MaterialBase64 = TestKeys.NewKey() });
        var codec = new PublicIdCodec(PublicIdKeyring.Create(rotated, TestProtocols.Test), TestProtocols.Test);

        Assert.True(codec.TryUnprotect(Domain, old, out var id));
        Assert.Equal(9, id);
        Assert.StartsWith("m1_k2_", codec.Protect(Domain, 9), StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_naming_a_key_outside_the_ring_never_falls_back()
    {
        var foreign = new PublicIdCodec(PublicIdKeyring.Create(TestKeys.Ring(new PublicIdKeyringOptions(), "k9"), TestProtocols.Test), TestProtocols.Test);

        Assert.False(_codec.TryUnprotect(Domain, foreign.Protect(Domain, 1), out _));
    }

    [Fact]
    public void A_token_minted_for_one_family_or_prefix_fails_elsewhere()
    {
        var alternate = new PublicIdCodec(PublicIdKeyring.Create(RingOptions, TestProtocols.Alternate), TestProtocols.Alternate);
        var token = _codec.Protect(Domain, 5);

        Assert.False(_codec.TryUnprotect("user", token, out _));
        Assert.NotEqual(token, _codec.Protect("user", 5));
        Assert.False(alternate.TryUnprotect(Domain, token, out _));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("43")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("m1_k1_")]
    [InlineData("m1_K1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("m2_k1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("m1_k1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("m1_k1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("m1_k1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("m1_k1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Every_malformation_is_refused_with_the_same_signal(string? token)
    {
        Assert.False(_codec.TryUnprotect(Domain, token, out var id));
        Assert.Equal(0, id);
    }

    [Fact]
    public void A_single_flipped_character_never_authenticates()
    {
        var token = _codec.Protect(Domain, 77);
        var flipped = token[..^2] + (token[^2] == 'A' ? 'B' : 'A') + token[^1];

        Assert.False(_codec.TryUnprotect(Domain, flipped, out _));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Minting_an_id_below_one_is_a_precondition_violation(long id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _codec.Protect(Domain, id));
    }

    [Theory]
    [InlineData("Record")]
    [InlineData("1record")]
    [InlineData("rec ord")]
    [InlineData("")]
    public void A_malformed_domain_is_a_programming_error(string domain)
    {
        Assert.Throws<ArgumentException>(() => _codec.Protect(domain, 1));
        Assert.Throws<ArgumentException>(() => _codec.TryUnprotect(domain, "x", out _));
    }

    [Fact]
    public void A_ring_with_a_key_id_that_cannot_travel_refuses_to_build()
    {
        var ring = TestKeys.Ring(new PublicIdKeyringOptions(), "Key_1");

        Assert.Throws<InvalidOperationException>(() => PublicIdKeyring.Create(ring, TestProtocols.Test));
        Assert.Contains("[a-z0-9]{1,8}", PublicIdKeyring.Validate(ring).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_route_constraint_matches_the_token_shape_only()
    {
        var constraint = new PublicIdRouteConstraint();
        var token = _codec.Protect(Domain, 3);

        Assert.True(Match(constraint, token));
        Assert.True(Match(constraint, "m1_k9_" + new string('A', PublicIdFormat.PayloadCharacters)));
        Assert.False(Match(constraint, "42"));
        Assert.False(Match(constraint, 42));
        Assert.False(constraint.Match(null, null, "id", new Microsoft.AspNetCore.Routing.RouteValueDictionary(), Microsoft.AspNetCore.Routing.RouteDirection.IncomingRequest));
    }

    private static bool Match(PublicIdRouteConstraint constraint, object value) =>
        constraint.Match(null, null, "id", new Microsoft.AspNetCore.Routing.RouteValueDictionary { ["id"] = value }, Microsoft.AspNetCore.Routing.RouteDirection.IncomingRequest);
}
