using System.Net;
using Codelab.Errors;
using Codelab.Integration.Classification;
using Codelab.Security.PublicIds;
using Microsoft.Extensions.DependencyInjection;
using Package.Sample.Api.Archive;
using Package.Sample.Api.Hosting;
using Package.Sample.Api.Records;
using Package.Sample.Tests.Support;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>Exit criterion: an outbound call to a server with an untrusted certificate is refused before any request.</summary>
public sealed class OutboundCertificateTests : IDisposable
{
    private readonly UntrustedTlsServer _upstream;
    private readonly SampleHost _host;

    public OutboundCertificateTests()
    {
        _upstream = UntrustedTlsServer.Start();
        _host = new SampleHost(SampleKeys.Create(), settings => SampleSettings.PointArchiveAt(settings, _upstream.BaseAddress));
    }

    public void Dispose()
    {
        _host.Dispose();
        _upstream.Dispose();
    }

    [Fact]
    public async Task The_archive_call_to_an_untrusted_server_is_a_502_envelope_and_never_reaches_the_server()
    {
        var session = _host.NewSession();
        var path = SampleRoutes.ArchiveFor(PublicId<RecordIds>.From(RecordStore.SeedRecordId).ToToken());

        var exchange = await session.SendAsync(HttpMethod.Post, path);

        var problem = await Problems.ReadAsync(exchange.Response, HttpStatusCode.BadGateway);
        Assert.Equal(ErrorCodes.Upstream, problem.GetProperty("code").GetString());
        Assert.Equal(0, _upstream.Requests);
    }

    [Fact]
    public async Task The_typed_client_classifies_the_refusal_as_a_rejected_certificate()
    {
        using var scope = _host.Services.CreateScope();
        var archive = scope.ServiceProvider.GetRequiredService<ArchiveClient>();

        var failure = await Assert.ThrowsAsync<UpstreamUnavailableException>(
            () => archive.ArchiveAsync(PublicId<RecordIds>.From(RecordStore.SeedRecordId), CancellationToken.None));

        Assert.Equal(ArchiveClient.Name, failure.Upstream);
        var classified = OutboundFailureClassifier.Classify(failure.InnerException!);
        Assert.Equal(OutboundFailureKind.CertificateRejected, classified.Kind);
        Assert.False(classified.IsTransient);
        Assert.Equal(0, _upstream.Requests);
    }
}
