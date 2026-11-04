using Codelab.Integration.Certificates;
using Codelab.Integration.Outbound;
using Codelab.Integration.Tests.Support;

namespace Codelab.Integration.Tests.Outbound;

public sealed class OutboundClientOptionsValidatorTests
{
    private const string Name = "upstream";
    private static readonly string ValidThumbprint = new('A', 64);
    private readonly OutboundClientOptionsValidator _validator = new();

    [Fact]
    public void Defaults_with_an_https_address_are_valid()
    {
        var result = _validator.Validate(Name, Valid());

        Assert.True(result.Succeeded);
    }

    public static TheoryData<string, Action<OutboundClientOptions>> InvalidCases() => new()
    {
        { "BaseAddress is required", options => options.BaseAddress = null },
        { "absolute URI", options => options.BaseAddress = new Uri("/relative", UriKind.Relative) },
        { "must use https", options => options.BaseAddress = new Uri("http://plain.example.test/") },
        { "must use https", options => options.BaseAddress = new Uri("ftp://files.example.test/") },
        { "MaxRetries", options => options.Retry.MaxRetries = -1 },
        { "MaxRetries", options => options.Retry.MaxRetries = OutboundDefaults.MaxRetriesCeiling + 1 },
        { "BaseDelay", options => options.Retry.BaseDelay = TimeSpan.FromMilliseconds(-1) },
        { "BaseDelay", options => options.Retry.MaxDelay = options.Retry.BaseDelay - TimeSpan.FromMilliseconds(1) },
        { "AttemptTimeout", options => options.Timeout.AttemptTimeout = TimeSpan.FromMilliseconds(1) },
        { "TotalTimeout", options => options.Timeout.TotalTimeout = options.Timeout.AttemptTimeout - TimeSpan.FromSeconds(1) },
        { "SystemTrust", options => options.Certificate.PinnedThumbprints.Add(ValidThumbprint) },
        { "SystemTrust", options => options.Certificate.AuthorityBundlePath = "ca.pem" },
        { "needs AuthorityBundlePath", options => options.Certificate.Mode = CertificateTrustMode.PinnedAuthority },
        { "does not exist", options => PinBundle(options, Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pem")) },
        { "at least one", options => options.Certificate.Mode = CertificateTrustMode.PinnedLeaf },
        { "SHA-256 thumbprint", options => PinLeaf(options, "not-a-thumbprint") },
        { "not a known trust mode", options => options.Certificate.Mode = (CertificateTrustMode)0 },
    };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_options_fail_with_a_message_naming_the_client_and_the_setting(string expected, Action<OutboundClientOptions> breakIt)
    {
        var options = Valid();
        breakIt(options);

        var result = _validator.Validate(Name, options);

        Assert.True(result.Failed);
        Assert.Contains(expected, result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains($"'{Name}'", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_http_is_accepted_only_when_allowed_explicitly()
    {
        var options = Valid();
        options.BaseAddress = new Uri("http://plain.example.test/");
        options.AllowPlainHttp = true;

        Assert.True(_validator.Validate(Name, options).Succeeded);
    }

    [Fact]
    public void Pinned_modes_with_their_anchor_are_valid()
    {
        using var authority = TestCertificates.CreateAuthority("Validator Root");
        var authorityPinned = Valid();
        authorityPinned.Certificate.Mode = CertificateTrustMode.PinnedAuthority;
        authorityPinned.Certificate.AuthorityCertificates.Add(authority);
        var leafPinned = Valid();
        PinLeaf(leafPinned, string.Join(':', ValidThumbprint.Chunk(2).Select(pair => new string(pair))).ToLowerInvariant());

        Assert.True(_validator.Validate(Name, authorityPinned).Succeeded);
        Assert.True(_validator.Validate(Name, leafPinned).Succeeded);
    }

    [Fact]
    public void Every_failure_is_reported_in_one_run()
    {
        var options = Valid();
        options.BaseAddress = null;
        options.Retry.MaxRetries = -1;

        var result = _validator.Validate(Name, options);

        Assert.Equal(2, result.Failures!.Count());
    }

    private static OutboundClientOptions Valid() => new() { BaseAddress = new Uri("https://upstream.example.test/") };

    private static void PinBundle(OutboundClientOptions options, string path)
    {
        options.Certificate.Mode = CertificateTrustMode.PinnedAuthority;
        options.Certificate.AuthorityBundlePath = path;
    }

    private static void PinLeaf(OutboundClientOptions options, string thumbprint)
    {
        options.Certificate.Mode = CertificateTrustMode.PinnedLeaf;
        options.Certificate.PinnedThumbprints.Add(thumbprint);
    }
}
