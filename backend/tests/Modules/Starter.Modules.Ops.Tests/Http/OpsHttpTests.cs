using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Starter.Modules.Ops.Api;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Infrastructure.Persistence;
using Starter.Modules.Ops.Tests.Fakes;

namespace Starter.Modules.Ops.Tests.Http;

/// <summary>One throwaway database and one open Ops host for the HTTP tests; both are removed at the end.</summary>
public sealed class OpsHttpFixture : IAsyncLifetime
{
    private TestDatabase? _database;
    private OpsHttpHost? _host;
    private string? _certificatePath;

    public TestDatabase Database => _database ?? throw new InvalidOperationException("Not started.");

    public OpsHttpHost Host => _host ?? throw new InvalidOperationException("Not started.");

    public static DateTimeOffset CertificateNotAfter { get; } = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _certificatePath = Path.Combine(Path.GetTempPath(), $"starter-ops-http-{Guid.NewGuid():N}.cer");
        using (var certificate = TestCertificates.SelfSigned("CN=starter-api-tls", CertificateNotAfter.AddYears(-1), CertificateNotAfter))
        {
            await File.WriteAllBytesAsync(_certificatePath, certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));
        }

        _host = await OpsHttpHost.StartAsync(_database.ConnectionString, open: true, new Dictionary<string, string?>
        {
            ["Codelab:Ops:Certificates:0:Name"] = "api-tls",
            ["Codelab:Ops:Certificates:0:Path"] = _certificatePath,
        });
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }
        }
        finally
        {
            if (_database is not null)
            {
                await _database.DisposeAsync();
            }

            if (_certificatePath is not null)
            {
                File.Delete(_certificatePath);
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OpsHttpGroup : ICollectionFixture<OpsHttpFixture>
{
    public const string Name = "ops http";
}

[Collection(OpsHttpGroup.Name)]
public sealed class OpsHttpTests(OpsHttpFixture fixture)
{
    private const string HealthPath = "/api/ops/health";
    private const string JobsPath = "/api/ops/jobs";
    private const string CertificatesPath = "/api/ops/certificates";
    private const string Operator = "CORP\\ops.operator";

    private OpsHttpHost Host => fixture.Host;

    [Fact]
    public async Task A_valid_ES256_assertion_from_the_Ops_Console_is_accepted_and_the_operator_is_recorded()
    {
        var response = await SendAsync(HealthPath, Host.Console);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        var database = body.GetProperty("components").EnumerateArray().Single(component => component.GetProperty("name").GetString() == "database");
        Assert.Equal(1, database.GetProperty("status").GetInt32());
        var correlationId = response.Headers.GetValues(Codelab.Observability.CorrelationId.Header).Single();
        var access = await ReadAsync(context => context.OperatorAccess.SingleAsync(row => row.CorrelationId == correlationId));
        Assert.Equal((Operator, OpsHttpHost.ConsoleService, OpsEndpoint.Health), (access.Operator, access.CallerService, access.Endpoint));
    }

    [Fact]
    public async Task Jobs_and_certificates_answer_for_the_console()
    {
        var jobs = await JsonAsync(await SendAsync(JobsPath, Host.Console));
        var certificates = await JsonAsync(await SendAsync(CertificatesPath, Host.Console));

        Assert.Contains(jobs.EnumerateArray(), job => job.GetProperty("key").GetString() == "ops-reference-code-purge");
        var certificate = Assert.Single(certificates.EnumerateArray());
        Assert.Equal("api-tls", certificate.GetProperty("name").GetString());
        Assert.Equal("CN=starter-api-tls", certificate.GetProperty("subject").GetString());
        Assert.Equal(OpsHttpFixture.CertificateNotAfter, certificate.GetProperty("notAfter").GetDateTimeOffset());
        Assert.InRange(certificate.GetProperty("status").GetInt32(), 1, 4);
    }

    [Fact]
    public async Task A_bearer_token_valid_in_this_host_is_401_on_the_Ops_surface()
    {
        using var probe = new HttpRequestMessage(HttpMethod.Get, OpsHttpHost.BearerProbePath);
        probe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OpsHttpHost.ValidBearerToken);
        using var ops = new HttpRequestMessage(HttpMethod.Get, HealthPath);
        ops.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OpsHttpHost.ValidBearerToken);
        ops.Headers.Add(OpsHeaders.Operator, Operator);

        Assert.Equal(HttpStatusCode.OK, (await Host.Client.SendAsync(probe)).StatusCode);
        await AssertUnauthorizedProblemAsync(await Host.Client.SendAsync(ops));
    }

    [Fact]
    public async Task An_api_key_header_valid_in_this_host_is_401_on_the_Ops_surface()
    {
        using var probe = new HttpRequestMessage(HttpMethod.Get, OpsHttpHost.ApiKeyProbePath);
        probe.Headers.Add(OpsHttpHost.ApiKeyHeader, OpsHttpHost.ValidApiKey);
        using var ops = new HttpRequestMessage(HttpMethod.Get, HealthPath);
        ops.Headers.Add(OpsHttpHost.ApiKeyHeader, OpsHttpHost.ValidApiKey);
        ops.Headers.Add(OpsHeaders.Operator, Operator);

        Assert.Equal(HttpStatusCode.OK, (await Host.Client.SendAsync(probe)).StatusCode);
        await AssertUnauthorizedProblemAsync(await Host.Client.SendAsync(ops));
    }

    [Fact]
    public async Task A_replayed_assertion_is_401()
    {
        var assertion = Host.Assertion(Host.Console, HttpMethod.Get, JobsPath);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(JobsPath, assertion)).StatusCode);
        await AssertUnauthorizedProblemAsync(await SendAsync(JobsPath, assertion));
    }

    [Fact]
    public async Task No_credential_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HealthPath);
        request.Headers.Add(OpsHeaders.Operator, Operator);

        await AssertUnauthorizedProblemAsync(await Host.Client.SendAsync(request));
    }

    [Fact]
    public async Task An_assertion_bound_to_another_path_is_401()
    {
        var assertion = Host.Assertion(Host.Console, HttpMethod.Get, JobsPath);

        await AssertUnauthorizedProblemAsync(await SendAsync(HealthPath, assertion));
    }

    [Fact]
    public async Task Another_trusted_service_is_403()
    {
        var response = await SendAsync(HealthPath, Host.Gateway);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_console_call_without_the_operator_is_400_and_runs_nothing()
    {
        var response = await SendAsync(HealthPath, Host.Assertion(Host.Console, HttpMethod.Get, HealthPath), operatorName: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OpsErrorCodes.OperatorRequired, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_reference_code_from_an_error_response_leads_back_to_its_request()
    {
        const string badLookup = "/api/ops/reference-codes/not-a-code";
        var failed = await SendAsync(badLookup, Host.Console);
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        var problem = await JsonAsync(failed);
        Assert.Equal(OpsErrorCodes.ReferenceCodeInvalid, problem.GetProperty("code").GetString());
        var referenceCode = problem.GetProperty("referenceCode").GetString()!;
        var correlationId = problem.GetProperty("correlationId").GetString()!;

        var found = await LookUpWhenIndexedAsync(referenceCode);

        Assert.Equal(referenceCode, found.GetProperty("referenceCode").GetString());
        Assert.Equal(correlationId, found.GetProperty("correlationId").GetString());
        Assert.Equal(correlationId, found.GetProperty("traceId").GetString());
        var request = found.GetProperty("request");
        Assert.Equal(("GET", badLookup, 400), (request.GetProperty("method").GetString(), request.GetProperty("path").GetString(), request.GetProperty("status").GetInt32()));
        Assert.True(found.GetProperty("searchFrom").GetDateTimeOffset() < found.GetProperty("searchTo").GetDateTimeOffset());
    }

    [Fact]
    public async Task An_unknown_reference_code_is_404_problem_json()
    {
        const string path = "/api/ops/reference-codes/00000-00000";

        var response = await SendAsync(path, Host.Console);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(OpsErrorCodes.ReferenceCodeNotFound, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_closed_host_without_a_trusted_caller_refuses_even_a_valid_assertion()
    {
        await using var closed = await OpsHttpHost.StartAsync(fixture.Database.ConnectionString, open: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, HealthPath);
        request.Headers.Add(closed.Protocol.ServiceAssertionHeaderName, closed.Assertion(closed.Console, HttpMethod.Get, HealthPath));
        request.Headers.Add(OpsHeaders.Operator, Operator);

        await AssertUnauthorizedProblemAsync(await closed.Client.SendAsync(request));
    }

    private async Task<JsonElement> LookUpWhenIndexedAsync(string referenceCode)
    {
        var path = $"/api/ops/reference-codes/{referenceCode}";
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
        while (true)
        {
            var response = await SendAsync(path, Host.Console);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return await JsonAsync(response);
            }

            Assert.True(TimeProvider.System.GetUtcNow() < deadline, $"{referenceCode} was not indexed in time (last status {(int)response.StatusCode}).");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private Task<HttpResponseMessage> SendAsync(string path, ServiceKey key) =>
        SendAsync(path, Host.Assertion(key, HttpMethod.Get, path));

    private async Task<HttpResponseMessage> SendAsync(string path, string assertion, string? operatorName = Operator)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(Host.Protocol.ServiceAssertionHeaderName, assertion);
        if (operatorName is not null)
        {
            request.Headers.Add(OpsHeaders.Operator, operatorName);
        }

        return await Host.Client.SendAsync(request);
    }

    private async Task<T> ReadAsync<T>(Func<OpsDbContext, Task<T>> read)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<OpsDbContext>());
    }

    private static async Task AssertUnauthorizedProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement;
    }
}
