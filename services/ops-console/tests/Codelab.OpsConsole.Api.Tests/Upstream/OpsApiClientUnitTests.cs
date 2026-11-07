using System.Net;
using System.Text;
using Codelab.Errors;
using Codelab.OpsConsole.Api.Upstream;
using Codelab.OpsConsole.Application.Certificates;
using Codelab.OpsConsole.Application.Ports;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codelab.OpsConsole.Api.Tests.Upstream;

public sealed class OpsApiClientUnitTests
{
    private static readonly OperatorName Alice = new("CORP\\alice");

    [Theory]
    [InlineData("CORP\\alice", "CORP\\alice")]
    [InlineData("alice@corp.example", "alice@corp.example")]
    [InlineData("CORP\\سارة", "CORP%5C%D8%B3%D8%A7%D8%B1%D8%A9")]
    [InlineData("CORP\\100%", "CORP%5C100%25")]
    public void The_operator_header_is_ascii_or_percent_encoded(string name, string header) =>
        Assert.Equal(header, OperatorHeader.ValueFor(new OperatorName(name)));

    [Fact]
    public void An_operator_name_is_trimmed_and_never_empty()
    {
        Assert.Equal("CORP\\alice", new OperatorName("  CORP\\alice ").Value);
        Assert.Throws<ArgumentException>(() => new OperatorName(" "));
    }

    [Fact]
    public void A_reference_code_route_is_escaped_defensively() =>
        Assert.Equal("api/ops/reference-codes/A%2FB", OpsApiRoutes.ReferenceCode("A/B"));

    [Fact]
    public async Task Each_call_names_the_operator_and_uses_a_relative_ops_route()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "{\"ok\":true}");

        var health = await Client(stub).GetHealthAsync(Alice, CancellationToken.None);

        Assert.True(health.GetProperty("ok").GetBoolean());
        Assert.Equal("https://api.example.test/portal/api/ops/health", stub.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("CORP\\alice", stub.Request.Headers.GetValues(OperatorHeader.Name).Single());
    }

    [Fact]
    public async Task An_unknown_reference_code_is_null()
    {
        var stub = new StubHandler(HttpStatusCode.NotFound, string.Empty);

        Assert.Null(await Client(stub).FindReferenceCodeAsync("9C1E5-B7A20", Alice, CancellationToken.None));
    }

    [Fact]
    public async Task Reference_code_facts_tolerate_missing_members()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "{\"correlationId\":\"abc\",\"request\":{\"method\":\"GET\"}}");

        var facts = await Client(stub).FindReferenceCodeAsync("9C1E5-B7A20", Alice, CancellationToken.None);

        Assert.Equal("abc", facts!.CorrelationId);
        Assert.Null(facts.Request);
        Assert.Null(facts.FirstSeen);
    }

    [Fact]
    public async Task Certificates_are_read_with_their_integer_status()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "[{\"name\":\"idp\",\"subject\":\"CN=idp\",\"notAfter\":\"2026-11-28T00:00:00Z\",\"daysLeft\":-1,\"status\":4}]");

        var certificate = Assert.Single(await Client(stub).GetCertificatesAsync(Alice, CancellationToken.None));

        Assert.Equal(CertificateStatus.Expired, certificate.Status);
        Assert.Equal(-1, certificate.DaysLeft);
    }

    [Fact]
    public async Task An_unreadable_certificate_keeps_its_finding_code_and_no_dates()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "[{\"name\":\"idp\",\"subject\":null,\"notAfter\":null,\"daysLeft\":null,\"status\":3,\"code\":\"OPS_CERTIFICATE_UNREADABLE\"}]");

        var certificate = Assert.Single(await Client(stub).GetCertificatesAsync(Alice, CancellationToken.None));

        Assert.Equal(CertificateStatus.Critical, certificate.Status);
        Assert.Null(certificate.NotAfter);
        Assert.Null(certificate.DaysLeft);
        Assert.Equal("OPS_CERTIFICATE_UNREADABLE", certificate.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}")]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task A_refused_or_unreadable_answer_is_upstream_unavailable(HttpStatusCode status, string body)
    {
        var stub = new StubHandler(status, body);

        var failure = await Assert.ThrowsAsync<UpstreamUnavailableException>(() => Client(stub).GetJobsAsync(Alice, CancellationToken.None));

        Assert.Equal(OpsApiClient.HttpClientName, failure.Upstream);
    }

    [Fact]
    public async Task A_certificate_list_of_the_wrong_shape_is_upstream_unavailable()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "{\"items\":[]}");

        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => Client(stub).GetCertificatesAsync(Alice, CancellationToken.None));
    }

    [Fact]
    public async Task An_unreachable_or_slow_api_is_upstream_unavailable_but_a_cancel_propagates()
    {
        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => Client(new StubHandler(new HttpRequestException("down"))).GetHealthAsync(Alice, CancellationToken.None));
        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => Client(new StubHandler(new TaskCanceledException("timeout"))).GetHealthAsync(Alice, CancellationToken.None));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(new StubHandler(new TaskCanceledException("cancel"))).GetHealthAsync(Alice, cancelled.Token));
    }

    private static OpsApiClient Client(StubHandler stub) =>
        new(new HttpClient(stub) { BaseAddress = new Uri("https://api.example.test/portal/") }, NullLogger<OpsApiClient>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = string.Empty;
        private readonly Exception? _failure;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public StubHandler(Exception failure) => _failure = failure;

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (_failure is not null)
            {
                throw _failure;
            }

            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
        }
    }
}
