using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Codelab.Security.Problems;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Sessions;
using Codelab.Security.Signing;
using Codelab.Security.Tests.Signing;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Codelab.Security.Tests.Sessions;

/// <summary>
/// Signing and sid revocation check the principal authorization uses: a user authenticated only by the endpoint
/// policy's named scheme (no default scheme), and a user token that carries a service claim, are both still checked.
/// </summary>
[Collection(SharedCodecGroup.Name)]
public sealed class AuthorizationPrincipalTests
{
    private const string ActionPath = "/api/user/action";
    private const string Body = "{\"value\":1}";

    private readonly string _master = TestKeys.NewKey();

    [Fact]
    public async Task A_user_authenticated_only_by_the_policy_named_scheme_must_sign_and_is_refused_after_revocation()
    {
        await using var host = await StartAsync(defaultScheme: null);

        await AssertRefusedAsync(await host.Client.SendAsync(Unsigned()), SecurityProblemCodes.SignatureMissing);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Signed(host))).StatusCode);

        await RevokeAsync(host);

        await AssertRefusedAsync(await host.Client.SendAsync(Signed(host)), SecurityProblemCodes.SessionRevoked);
    }

    [Fact]
    public async Task A_user_token_carrying_a_service_claim_is_still_a_user_for_signing_and_revocation()
    {
        await using var host = await StartAsync(defaultScheme: TestAuthentication.SchemeName);

        await AssertRefusedAsync(await host.Client.SendAsync(Unsigned(forgedServiceClaim: true)), SecurityProblemCodes.SignatureMissing);

        await RevokeAsync(host);

        await AssertRefusedAsync(await host.Client.SendAsync(Signed(host, forgedServiceClaim: true)), SecurityProblemCodes.SessionRevoked);
    }

    [Fact]
    public void Only_identities_issued_by_the_service_assertion_scheme_are_services()
    {
        var service = Identity(ServiceAssertionAuthenticationHandler.SchemeName, withServiceClaim: true);
        var forgedUser = Identity(TestAuthentication.SchemeName, withServiceClaim: true);
        var serviceSchemeWithoutClaim = Identity(ServiceAssertionAuthenticationHandler.SchemeName, withServiceClaim: false);

        Assert.True(SessionClaims.IsService(new ClaimsPrincipal(service)));
        Assert.False(SessionClaims.IsService(new ClaimsPrincipal(forgedUser)));
        Assert.False(SessionClaims.IsService(new ClaimsPrincipal(serviceSchemeWithoutClaim)));
        Assert.False(SessionClaims.IsService(new ClaimsPrincipal([service, forgedUser])));
        Assert.False(SessionClaims.IsService(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private static ClaimsIdentity Identity(string authenticationType, bool withServiceClaim)
    {
        var claims = new List<Claim> { new(SecurityClaimTypes.Subject, "caller") };
        if (withServiceClaim)
        {
            claims.Add(new Claim(SecurityClaimTypes.Service, "caller"));
        }

        return new ClaimsIdentity(claims, authenticationType);
    }

    private Task<SecurityTestHost> StartAsync(string? defaultScheme)
    {
        var options = SigningHost.Options(SecurityEnforcementMode.Enforce, _master);
        options.DefaultScheme = defaultScheme;
        options.MapEndpoints = endpoints => endpoints
            .MapPost(ActionPath, (HttpContext context) => TypedResults.Text("ran for " + context.User.FindFirst(SecurityClaimTypes.Subject)?.Value))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = TestAuthentication.SchemeName });
        return SecurityTestHost.StartAsync(options);
    }

    private static HttpRequestMessage Unsigned(bool forgedServiceClaim = false)
    {
        var request = SigningHost.Request(HttpMethod.Post, ActionPath, Body);
        AddForgedClaim(request, forgedServiceClaim);
        return request;
    }

    private HttpRequestMessage Signed(SecurityTestHost host, bool forgedServiceClaim = false)
    {
        var request = SigningHost.Request(HttpMethod.Post, ActionPath, Body);
        AddForgedClaim(request, forgedServiceClaim);
        SigningHost.Signer(_master).Sign(request, host.Clock.GetUtcNow(), Encoding.UTF8.GetBytes(Body));
        return request;
    }

    private static void AddForgedClaim(HttpRequestMessage request, bool forgedServiceClaim)
    {
        if (forgedServiceClaim)
        {
            request.Headers.Add(TestAuthentication.ServiceClaimHeader, "forged-service");
        }
    }

    private static Task RevokeAsync(SecurityTestHost host) =>
        host.Services.GetRequiredService<ISessionRevocationStore>()
            .RevokeAsync(new SessionRevocation(SigningHost.Session.SessionId, host.Clock.GetUtcNow().AddHours(1)), CancellationToken.None);

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }
}
