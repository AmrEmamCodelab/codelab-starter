using Microsoft.Extensions.Options;
using Starter.Api.Tests.Support;

namespace Starter.Api.Tests.SecurityEdge;

/// <summary>The CORS origin check stops a host composed like Program.cs before it starts.</summary>
[Collection(HostTestGroup.Name)]
public sealed class CorsBootHttpTests
{
    private const string FirstOrigin = "Codelab:Security:Cors:AllowedOrigins:0";
    private const string SecondOrigin = "Codelab:Security:Cors:AllowedOrigins:1";

    [Fact]
    public void A_duplicated_origin_stops_the_boot()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => SecurityEdgeHost.Build(new CapturingSink(), new Dictionary<string, string?>
        {
            [SecondOrigin] = SecurityEdgeHost.AllowedOrigin,
        }));

        Assert.Contains(failure.Failures, problem => problem.Contains("2 times", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://portal.example.test/")]
    [InlineData("https://portal.example.test/app")]
    [InlineData("*")]
    public void A_bad_origin_stops_the_boot(string origin)
    {
        var failure = Assert.Throws<OptionsValidationException>(() => SecurityEdgeHost.Build(new CapturingSink(), new Dictionary<string, string?>
        {
            [FirstOrigin] = origin,
        }));

        Assert.Contains(failure.Failures, problem => problem.Contains(origin, StringComparison.Ordinal));
    }

    [Fact]
    public void A_plain_http_origin_stops_the_boot_outside_Development()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => SecurityEdgeHost.Build(new CapturingSink(), environment: "Staging"));

        Assert.Contains(failure.Failures, problem => problem.Contains("https", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_valid_origin_list_starts()
    {
        await using var app = await SecurityEdgeHost.StartAsync(new CapturingSink(), new Dictionary<string, string?>
        {
            [SecondOrigin] = "https://portal.example.test",
        });

        Assert.NotNull(app);
    }
}
