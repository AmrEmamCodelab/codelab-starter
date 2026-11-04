using System.Security.Claims;
using Codelab.Observability.Tests.Support;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Codelab.Observability.Tests;

/// <summary>The keyed user hash: configured key, no committed default, fail fast in Production.</summary>
public sealed class UserIdHashTests
{
    [Fact]
    public void Same_user_and_key_give_the_same_sixteen_hex_value()
    {
        var key = TestHosts.NewHashKey();
        var first = UserIdHash.Create(new UserIdHashSettings { Key = key }, Env(Environments.Production));
        var second = UserIdHash.Create(new UserIdHashSettings { Key = key }, Env(Environments.Production));

        var hash = first.Of("user-17");

        Assert.Equal(hash, second.Of("user-17"));
        Assert.Matches("^[0-9a-f]{16}$", hash);
        Assert.False(first.IsEphemeral);
    }

    [Fact]
    public void Different_keys_give_different_values()
    {
        var first = UserIdHash.Create(new UserIdHashSettings { Key = TestHosts.NewHashKey() }, Env(Environments.Production));
        var second = UserIdHash.Create(new UserIdHashSettings { Key = TestHosts.NewHashKey() }, Env(Environments.Production));

        Assert.NotEqual(first.Of("user-17"), second.Of("user-17"));
    }

    [Fact]
    public void A_missing_key_in_Production_refuses_to_start_and_names_the_key()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => UserIdHash.Create(new UserIdHashSettings(), Env(Environments.Production)));

        Assert.Contains(UserIdHashSettings.KeyName, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void A_missing_key_elsewhere_uses_a_random_per_process_key(string environment)
    {
        var first = UserIdHash.Create(new UserIdHashSettings(), Env(environment));
        var second = UserIdHash.Create(new UserIdHashSettings(), Env(environment));

        Assert.True(first.IsEphemeral);
        Assert.NotEqual(first.Of("user-17"), second.Of("user-17"));
    }

    [Fact]
    public void A_key_that_is_not_base64_is_refused_without_echoing_it()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => UserIdHash.Create(new UserIdHashSettings { Key = "not base64 !!" }, Env(Environments.Development)));

        Assert.DoesNotContain("not base64", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_key_is_refused()
    {
        var shortKey = Convert.ToBase64String(new byte[UserIdHash.MinimumKeyBytes - 1]);

        Assert.Throws<InvalidOperationException>(
            () => UserIdHash.Create(new UserIdHashSettings { Key = shortKey }, Env(Environments.Development)));
    }

    [Fact]
    public void A_principal_is_hashed_by_its_name_identifier_and_anonymous_is_null()
    {
        var hash = UserIdHash.Create(new UserIdHashSettings { Key = TestHosts.NewHashKey() }, Env(Environments.Production));
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-17")], "test"));

        Assert.Equal(hash.Of("user-17"), hash.Of(user));
        Assert.Null(hash.Of(new ClaimsPrincipal()));
        Assert.Null(hash.Of((ClaimsPrincipal?)null));
        Assert.Null(hash.Of(" "));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void A_published_key_outside_Development_refuses_to_start_without_echoing_it(string environment)
    {
        var key = TestHosts.NewHashKey();
        var denylist = PublishedKeyDenylist.FromKeyDigests([Sha256Hex(Convert.FromBase64String(key))]);

        var error = Assert.Throws<InvalidOperationException>(
            () => UserIdHash.Create(new UserIdHashSettings { Key = key }, Env(environment), denylist));

        Assert.Contains("PUBLISHED key material", error.Message, StringComparison.Ordinal);
        Assert.Contains(UserIdHashSettings.KeyName, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_published_key_is_recognised_as_written_too_and_Development_may_still_use_it()
    {
        var key = TestHosts.NewHashKey();
        var denylist = PublishedKeyDenylist.FromKeyDigests([Sha256Hex(System.Text.Encoding.UTF8.GetBytes(key))]);

        Assert.True(denylist.IsPublished(key, Convert.FromBase64String(key)));
        Assert.False(UserIdHash.Create(new UserIdHashSettings { Key = key }, Env(Environments.Development), denylist).IsEphemeral);
        Assert.False(PublishedKeyDenylist.Embedded.IsPublished(key, Convert.FromBase64String(key)));
    }

    [Fact]
    public void The_embedded_list_refuses_every_starter_fixture_key_in_Production()
    {
        var fixtureKeys = PublishedKeys.FixtureRingMaterials();

        Assert.NotEmpty(fixtureKeys);
        Assert.All(fixtureKeys, key => Assert.Throws<InvalidOperationException>(
            () => UserIdHash.Create(new UserIdHashSettings { Key = key }, Env(Environments.Production))));
    }

    [Fact]
    public void The_embedded_list_holds_every_harvest_source_key_digest()
    {
        // Digest matching only (as Codelab.Security's PublishedFixtureKeyTests): no harvest key value exists in this repo.
        var digests = PublishedKeys.HarvestKeyDigests();

        Assert.NotEmpty(digests);
        Assert.All(digests, digest => Assert.True(PublishedKeyDenylist.Embedded.ContainsKeyDigest(digest), "published-material.source.json is not linked into Codelab.Observability"));
    }

    internal static IHostEnvironment Env(string name) => new TestEnvironment(name);

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = TestHosts.ServiceName;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
