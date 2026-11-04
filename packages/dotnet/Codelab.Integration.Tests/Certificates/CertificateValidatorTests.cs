using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Codelab.Integration.Certificates;
using Codelab.Integration.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Integration.Tests.Certificates;

/// <summary>The pinned validators, called directly with run-time certificates.</summary>
public sealed class CertificateValidatorTests
{
    private const SslPolicyErrors ChainErrors = SslPolicyErrors.RemoteCertificateChainErrors;

    [Fact]
    public void System_trust_installs_no_callback_at_all()
    {
        var options = new OutboundCertificateOptions();

        Assert.Null(ServerCertificatePolicy.CreateCallback(options, TimeProvider.System));
        using var handler = ServerCertificatePolicy.CreatePrimaryHandler(options, TimeProvider.System);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Equal(X509RevocationMode.NoCheck, handler.SslOptions.CertificateRevocationCheckMode);
    }

    [Fact]
    public void Revocation_checking_is_switched_on_by_the_option()
    {
        var options = new OutboundCertificateOptions { CheckRevocation = true };

        using var handler = ServerCertificatePolicy.CreatePrimaryHandler(options, TimeProvider.System);

        Assert.Equal(X509RevocationMode.Online, handler.SslOptions.CertificateRevocationCheckMode);
    }

    [Fact]
    public void Pinned_authority_accepts_its_own_server_despite_system_chain_errors()
    {
        using var authority = TestCertificates.CreateAuthority("Unit Root");
        using var server = TestCertificates.CreateServer(authority);
        var validator = AuthorityValidator(authority);

        Assert.True(validator.Validate(server, presentedChain: null, ChainErrors));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)]
    public void Pinned_authority_never_excuses_a_name_mismatch_or_a_missing_certificate(SslPolicyErrors errors)
    {
        using var authority = TestCertificates.CreateAuthority("Unit Root");
        using var server = TestCertificates.CreateServer(authority);

        Assert.False(AuthorityValidator(authority).Validate(server, presentedChain: null, errors));
    }

    [Fact]
    public void Pinned_authority_refuses_a_server_of_another_authority_and_a_null_certificate()
    {
        using var pinned = TestCertificates.CreateAuthority("Pinned Root");
        using var other = TestCertificates.CreateAuthority("Other Root");
        using var foreignServer = TestCertificates.CreateServer(other);
        var validator = AuthorityValidator(pinned);

        Assert.False(validator.Validate(foreignServer, presentedChain: null, ChainErrors));
        Assert.False(validator.Validate(certificate: null, presentedChain: null, ChainErrors));
    }

    [Fact]
    public void Pinned_authority_refuses_an_expired_server_certificate()
    {
        var past = new FakeTimeProvider(TimeProvider.System.GetUtcNow().AddYears(-1));
        using var authority = TestCertificates.CreateAuthority("Unit Root");
        using var expired = TestCertificates.CreateServer(authority, past);

        Assert.False(AuthorityValidator(authority).Validate(expired, presentedChain: null, ChainErrors));
    }

    [Fact]
    public void Pinned_leaf_accepts_only_the_pinned_thumbprint_within_its_validity()
    {
        using var server = TestCertificates.CreateSelfSignedServer();
        using var other = TestCertificates.CreateSelfSignedServer();
        var validator = LeafValidator(server, TimeProvider.System);

        Assert.True(validator.Validate(server, presentedChain: null, ChainErrors));
        Assert.False(validator.Validate(other, presentedChain: null, ChainErrors));
        Assert.False(validator.Validate(server, presentedChain: null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(validator.Validate(certificate: null, presentedChain: null, ChainErrors));
    }

    [Fact]
    public void Pinned_leaf_refuses_its_certificate_once_it_has_expired()
    {
        using var server = TestCertificates.CreateSelfSignedServer();
        var later = new FakeTimeProvider(TimeProvider.System.GetUtcNow().AddDays(2));

        Assert.False(LeafValidator(server, later).Validate(server, presentedChain: null, SslPolicyErrors.None));
    }

    [Fact]
    public void Pinned_leaf_matches_thumbprints_written_with_separators_and_lower_case()
    {
        using var server = TestCertificates.CreateSelfSignedServer();
        var hex = server.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
        var withColons = string.Join(':', hex.Chunk(2).Select(pair => new string(pair)));
        var options = new OutboundCertificateOptions { Mode = CertificateTrustMode.PinnedLeaf };
        options.PinnedThumbprints.Add(withColons);

        var validator = ServerCertificatePolicy.CreateValidator(options, TimeProvider.System)!;

        Assert.True(validator.Validate(server, presentedChain: null, ChainErrors));
    }

    [Fact]
    public void An_undefined_trust_mode_is_refused_when_building_the_validator()
    {
        var options = new OutboundCertificateOptions { Mode = (CertificateTrustMode)99 };

        Assert.Throws<InvalidOperationException>(() => ServerCertificatePolicy.CreateValidator(options, TimeProvider.System));
    }

    private static IServerCertificateValidator AuthorityValidator(X509Certificate2 authority)
    {
        var options = new OutboundCertificateOptions { Mode = CertificateTrustMode.PinnedAuthority };
        options.AuthorityCertificates.Add(TestCertificates.PublicPart(authority));
        return ServerCertificatePolicy.CreateValidator(options, TimeProvider.System)!;
    }

    private static IServerCertificateValidator LeafValidator(X509Certificate2 leaf, TimeProvider clock)
    {
        var options = new OutboundCertificateOptions { Mode = CertificateTrustMode.PinnedLeaf };
        options.PinnedThumbprints.Add(leaf.GetCertHashString(HashAlgorithmName.SHA256));
        return ServerCertificatePolicy.CreateValidator(options, clock)!;
    }
}
