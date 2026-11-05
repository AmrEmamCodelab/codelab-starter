using System.Net;
using System.Text;
using Codelab.Testing.Contracts;
using Starter.Contract.Tests.Support;
using Starter.Tests.Hosting;

namespace Starter.Contract.Tests;

/// <summary>
/// Every error the host answers is Codelab.Errors problem+json: probes every endpoint with an anonymous client
/// (401 for protected endpoints, 400 for empty bodies), an unknown route (404), and an empty login body (400
/// validation). 403 and coded refusals need a seeded database; Starter.Integration.Tests asserts those.
/// </summary>
[Collection(ContractHostGroup.Name)]
public sealed class ProblemJsonContractTests(ContractHost host)
{
    private const string UnknownRoute = "/api/no-such-endpoint";
    private const string LoginRoute = "/api/auth/login";
    private const string DashboardRoute = "/api/dashboard";
    private const string JsonMediaType = "application/json";
    private const string EmptyJsonObject = "{}";
    private const string ValidationFailedCode = "VALIDATION_FAILED";

    [Fact]
    public async Task AllErrorsAreProblemJson()
    {
        using var client = host.Factory.CreateClient();

        (await ProblemJsonContract.AllErrorsAreProblemJsonAsync(client, host.Endpoints, StarterProblems.Options)).ThrowIfFailed();
    }

    [Fact]
    public async Task AProtectedEndpointWithoutCredentialsAnswers401ProblemJson()
    {
        using var client = host.Factory.CreateClient();
        using var response = await client.GetAsync(new Uri(DashboardRoute, UriKind.Relative));

        await StarterProblems.AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnUnknownRouteAnswers404ProblemJson()
    {
        using var client = host.Factory.CreateClient();
        using var response = await client.GetAsync(new Uri(UnknownRoute, UriKind.Relative));

        await StarterProblems.AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnInvalidBodyAnswers400ValidationProblemJson()
    {
        using var client = host.Factory.CreateClient();
        using var content = new StringContent(EmptyJsonObject, Encoding.UTF8, JsonMediaType);
        using var response = await client.PostAsync(new Uri(LoginRoute, UriKind.Relative), content);

        Assert.Equal(ValidationFailedCode, await StarterProblems.AssertProblemAsync(response, HttpStatusCode.BadRequest));
    }
}
