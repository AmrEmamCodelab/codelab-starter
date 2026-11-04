using Codelab.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Codelab.Errors.Tests;

/// <summary>The status table, the identity stamp, the normalizer and the scrubber, without a host.</summary>
public sealed class EnvelopeUnitTests
{
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, ErrorCodes.Malformed)]
    [InlineData(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated)]
    [InlineData(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden)]
    [InlineData(StatusCodes.Status404NotFound, ErrorCodes.NotFound)]
    [InlineData(StatusCodes.Status405MethodNotAllowed, ErrorCodes.Malformed)]
    [InlineData(StatusCodes.Status409Conflict, ErrorCodes.Conflict)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, ErrorCodes.PayloadTooLarge)]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, ErrorCodes.Malformed)]
    [InlineData(StatusCodes.Status422UnprocessableEntity, ErrorCodes.BusinessRule)]
    [InlineData(StatusCodes.Status429TooManyRequests, ErrorCodes.Retryable)]
    [InlineData(StatusCodes.Status502BadGateway, ErrorCodes.Upstream)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, ErrorCodes.Retryable)]
    [InlineData(StatusCodes.Status504GatewayTimeout, ErrorCodes.InternalError)]
    [InlineData(StatusCodes.Status418ImATeapot, ErrorCodes.Malformed)]
    public void Every_status_resolves_to_one_code(int status, string code)
    {
        Assert.Equal(code, FrameworkStatusCodes.Describe(status).Code);
        Assert.False(string.IsNullOrWhiteSpace(FrameworkStatusCodes.Describe(status).Detail));
    }

    [Fact]
    public void Stamping_writes_both_ids_removes_the_framework_trace_id_and_is_idempotent()
    {
        var context = new DefaultHttpContext();
        var extensions = new Dictionary<string, object?> { [ErrorEnvelope.FrameworkTraceIdMember] = "00-abc" };

        ErrorEnvelope.StampIdentity(extensions, context);
        ErrorEnvelope.StampIdentity(extensions, context);

        var correlationId = CorrelationId.For(context);
        Assert.Equal(correlationId, extensions[ErrorEnvelope.CorrelationIdMember]);
        Assert.Equal(ReferenceCode.From(correlationId), extensions[ErrorEnvelope.ReferenceCodeMember]);
        Assert.False(extensions.ContainsKey(ErrorEnvelope.FrameworkTraceIdMember));
    }

    [Fact]
    public void The_normalizer_labels_an_unlabelled_body_and_scrubs_it()
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            Instance = "/records/7",
            Detail = "Record 7 owned by jane@example.test was not found.",
            Extensions = { ["exception"] = "System.Exception: boom", ["traceId"] = "00-abc" },
        };

        ProblemDetailsNormalizer.Normalize(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });

        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal(ErrorCodes.NotFound, problem.Extensions[ErrorEnvelope.CodeMember]);
        Assert.Null(problem.Type);
        Assert.Null(problem.Instance);
        Assert.Equal(ErrorEnvelope.Title, problem.Title);
        Assert.Equal(FrameworkStatusCodes.Describe(StatusCodes.Status404NotFound).Detail, problem.Detail);
        Assert.Equal(
            [ErrorEnvelope.CodeMember, ErrorEnvelope.CorrelationIdMember, ErrorEnvelope.ReferenceCodeMember],
            problem.Extensions.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_body_that_carries_a_code_keeps_it_and_a_vetted_detail_survives()
    {
        var context = new DefaultHttpContext();
        ClientErrorScrubber.MarkVetted(context, "Vetted sentence.");
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Detail = "Vetted sentence.",
            Extensions = { [ErrorEnvelope.CodeMember] = "RECORD_NOT_A_DRAFT" },
        };

        ProblemDetailsNormalizer.Normalize(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });

        Assert.Equal("RECORD_NOT_A_DRAFT", problem.Extensions[ErrorEnvelope.CodeMember]);
        Assert.Equal("Vetted sentence.", problem.Detail);
    }

    [Fact]
    public void A_validation_body_with_errors_is_validation_failed()
    {
        var context = new DefaultHttpContext();
        var problem = new HttpValidationProblemDetails(new Dictionary<string, string[]> { ["name"] = ["A value is required."] })
        {
            Status = StatusCodes.Status400BadRequest,
        };

        ProblemDetailsNormalizer.Normalize(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });

        Assert.Equal(ErrorCodes.Validation, problem.Extensions[ErrorEnvelope.CodeMember]);
    }

    [Fact]
    public void The_scrubber_keeps_members_the_host_allows()
    {
        var context = new DefaultHttpContext();
        var problem = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Extensions = { ["retryAfter"] = 5, ["debug"] = "x" } };

        ClientErrorScrubber.Scrub(problem, context, new HashSet<string>(StringComparer.Ordinal) { "retryAfter" });

        Assert.True(problem.Extensions.ContainsKey("retryAfter"));
        Assert.False(problem.Extensions.ContainsKey("debug"));
    }

    [Fact]
    public void The_failure_bug_class_is_a_5xx_above_warning()
    {
        Assert.True(new Failure(StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "x", Microsoft.Extensions.Logging.LogLevel.Error).IsServerFault);
        Assert.False(new Failure(StatusCodes.Status503ServiceUnavailable, ErrorCodes.Retryable, "x", Microsoft.Extensions.Logging.LogLevel.Warning).IsServerFault);
        Assert.False(new Failure(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "x", Microsoft.Extensions.Logging.LogLevel.Error).IsServerFault);
    }
}
