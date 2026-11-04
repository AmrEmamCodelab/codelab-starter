using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codelab.Errors.Tests.Support;
using Codelab.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Codelab.Errors.Tests;

/// <summary>
/// Every error path of a Codelab host answers application/problem+json with <c>code</c>, <c>correlationId</c> and
/// <c>referenceCode</c> (the correlation id equal to the response header), and no exception text, type, framework id
/// or personal value ever reaches the body.
/// </summary>
public sealed class ErrorPipelineTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        (_app, _) = await ErrorHost.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("GET", TestPaths.Unclassified, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError)]
    [InlineData("GET", TestPaths.RuleBroken, StatusCodes.Status422UnprocessableEntity, ErrorCodes.BusinessRule)]
    [InlineData("GET", TestPaths.Upstream, StatusCodes.Status502BadGateway, ErrorCodes.Upstream)]
    [InlineData("GET", TestPaths.Busy, StatusCodes.Status503ServiceUnavailable, ErrorCodes.Retryable)]
    [InlineData("GET", TestPaths.Refused, StatusCodes.Status409Conflict, TestPaths.NotADraftCode)]
    [InlineData("GET", TestPaths.Secure, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated)]
    [InlineData("GET", "/no/such/route", StatusCodes.Status404NotFound, ErrorCodes.NotFound)]
    [InlineData("POST", TestPaths.Unclassified, StatusCodes.Status405MethodNotAllowed, ErrorCodes.Malformed)]
    [InlineData("GET", TestPaths.LeakyProblem, StatusCodes.Status400BadRequest, ErrorCodes.Malformed)]
    [InlineData("GET", TestPaths.MvcMissing, StatusCodes.Status404NotFound, ErrorCodes.NotFound)]
    [InlineData("GET", TestPaths.MvcLeakyProblem, StatusCodes.Status409Conflict, ErrorCodes.Conflict)]
    public async Task Every_error_path_answers_the_one_envelope(string method, string path, int status, string code)
    {
        using var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        await AssertEnvelopeAsync(response, status, code);
    }

    [Fact]
    public async Task A_wrong_content_type_is_unsupported_media_type()
    {
        using var content = new StringContent("name=x", Encoding.UTF8, "text/plain");
        using var response = await _client.PostAsync(new Uri(TestPaths.Json, UriKind.Relative), content);

        await AssertEnvelopeAsync(response, StatusCodes.Status415UnsupportedMediaType, ErrorCodes.Malformed);
    }

    [Fact]
    public async Task Invalid_mvc_fields_are_validation_failed_with_per_field_errors()
    {
        using var content = JsonBody("""{"name":null,"count":99}""");
        using var response = await _client.PostAsync(new Uri(TestPaths.MvcItems, UriKind.Relative), content);

        var body = await AssertEnvelopeAsync(response, StatusCodes.Status400BadRequest, ErrorCodes.Validation);
        var errors = body.GetProperty(ErrorEnvelope.ErrorsMember);
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Count", out _));
    }

    [Fact]
    public async Task An_unparseable_mvc_body_is_malformed_and_names_no_type()
    {
        using var content = JsonBody("""{"name":"x","count":"not-a-number"}""");
        using var response = await _client.PostAsync(new Uri(TestPaths.MvcItems, UriKind.Relative), content);

        var body = await AssertEnvelopeAsync(response, StatusCodes.Status400BadRequest, ErrorCodes.Malformed);
        var text = body.GetRawText();
        Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-number", text, StringComparison.Ordinal);
        Assert.Contains(ModelBindingErrors.UnparseableField, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_that_does_not_accept_json_still_gets_the_envelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TestPaths.Unclassified);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await _client.SendAsync(request);

        await AssertEnvelopeAsync(response, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError);
    }

    [Fact]
    public async Task A_transient_failure_says_when_to_retry()
    {
        using var response = await _client.GetAsync(new Uri(TestPaths.Busy, UriKind.Relative));

        Assert.Equal(TransientRetryAfter, response.Headers.RetryAfter?.Delta);
    }

    private static readonly TimeSpan TransientRetryAfter = TimeSpan.FromSeconds(Classifiers.TransientFaultClassifier.RetryAfterSeconds);

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    internal static async Task<JsonElement> AssertEnvelopeAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(ErrorEnvelope.ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);

        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;
        var correlationId = body.GetProperty(ErrorEnvelope.CorrelationIdMember).GetString()!;
        Assert.Equal(code, body.GetProperty(ErrorEnvelope.CodeMember).GetString());
        Assert.Equal(ReferenceCode.From(correlationId), body.GetProperty(ErrorEnvelope.ReferenceCodeMember).GetString());
        Assert.Equal(correlationId, response.Headers.GetValues(CorrelationId.Header).Single());
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.False(body.TryGetProperty(ErrorEnvelope.FrameworkTraceIdMember, out _));
        Assert.False(body.TryGetProperty("type", out _));
        Assert.False(body.TryGetProperty("exception", out _));
        Assert.DoesNotContain(TestPaths.PersonalValue, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
        return body;
    }
}
