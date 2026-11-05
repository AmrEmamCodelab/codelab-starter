using System.Net;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Tests.Support;

namespace Starter.DevIdp.Tests;

public sealed class InProcessServerTests
{
    [Fact]
    public async Task The_handler_serves_absolute_issuer_urls_as_the_api_named_client_would_call_them()
    {
        await using var server = await DevIdpInProcessServer.StartAsync();
        using var client = new HttpClient(server.CreateHandler());

        using var response = await client.GetAsync(new Uri(server.Issuer, OidcRoutes.Discovery));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Uri("http://localhost:5190"), server.Issuer);
    }

    [Fact]
    public async Task Configure_runs_before_validation_and_replaces_the_defaults()
    {
        await using var server = await DevIdpInProcessServer.StartAsync(options =>
        {
            options.Users.Clear();
            options.Users.Add(new Configuration.DevIdpUser { Subject = "only", Email = "only@example.test" });
        });

        Assert.Equal("only", Assert.Single(server.Options.Users).Subject);
    }

    [Fact]
    public async Task Incoherent_settings_fail_the_start()
    {
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            DevIdpInProcessServer.StartAsync(options => options.Clients.Clear()));
    }

    [Fact]
    public async Task The_browser_client_does_not_follow_redirects()
    {
        await using var idp = await DevIdpTestServer.StartAsync();

        using var response = await idp.AuthorizeAsync(DevIdpTestServer.AuthorizeParameters(DevIdpTestServer.NewVerifier()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
