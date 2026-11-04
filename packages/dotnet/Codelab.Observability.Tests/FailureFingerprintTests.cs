using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Codelab.Observability.Tests;

/// <summary>Fingerprints group one bug across ids and messages: type plus site, nothing else.</summary>
public sealed class FailureFingerprintTests
{
    [Fact]
    public void Is_twelve_lowercase_hex_and_stable()
    {
        var input = new FingerprintInput(typeof(TimeoutException), "GET records/{id}");

        var fingerprint = FailureFingerprint.For(input);

        Assert.Matches("^[0-9a-f]{12}$", fingerprint);
        Assert.Equal(fingerprint, FailureFingerprint.For(input with { }));
    }

    [Fact]
    public void Type_and_site_both_split_groups()
    {
        var baseline = FailureFingerprint.For(new FingerprintInput(typeof(TimeoutException), "GET records/{id}"));

        Assert.NotEqual(baseline, FailureFingerprint.For(new FingerprintInput(typeof(IOException), "GET records/{id}")));
        Assert.NotEqual(baseline, FailureFingerprint.For(new FingerprintInput(typeof(TimeoutException), "POST records/{id}")));
    }

    [Fact]
    public void A_blank_site_is_refused()
    {
        Assert.Throws<ArgumentException>(() => FailureFingerprint.For(new FingerprintInput(typeof(Exception), " ")));
    }

    [Fact]
    public void Request_site_is_the_uppercased_method_and_route_template()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "get";
        context.SetEndpoint(new RouteEndpoint(
            static _ => Task.CompletedTask, RoutePatternFactory.Parse("records/{id}"), order: 0, EndpointMetadataCollection.Empty, "records"));

        Assert.Equal("records/{id}", FailureFingerprint.RouteTemplateOf(context));
        Assert.Equal("GET records/{id}", FailureFingerprint.RequestSite(context));
    }

    [Fact]
    public void An_unrouted_request_has_the_unrouted_site()
    {
        Assert.Equal(FailureFingerprint.UnroutedSite, FailureFingerprint.RequestSite(new DefaultHttpContext()));
    }

    [Fact]
    public void Job_and_startup_sites_never_include_arguments()
    {
        Assert.Equal("job:FailureFingerprintTests.Run", FailureFingerprint.JobSite(typeof(FailureFingerprintTests), "Run"));
        Assert.Equal("startup:starter-api", FailureFingerprint.StartupSite("starter-api"));
    }
}
