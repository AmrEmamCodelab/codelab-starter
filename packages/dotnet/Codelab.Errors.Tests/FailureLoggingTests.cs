using System.Text.Json;
using Codelab.Errors.Tests.Support;
using Codelab.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace Codelab.Errors.Tests;

/// <summary>
/// A failure is logged ONCE, PII-free, with the correlation id and reference code the caller was shown, the code, the
/// classifier, the fingerprint and the user only as a hash; the job filter resolves codes through the same table.
/// </summary>
public sealed class FailureLoggingTests
{
    [Fact]
    public async Task The_failure_line_is_pii_free_and_carries_the_ids_the_caller_was_shown()
    {
        var (app, sink) = await ErrorHost.StartAsync();
        await using var _ = app;

        using var response = await app.GetTestClient().GetAsync(
            new Uri($"{TestPaths.Unclassified}?search={TestPaths.PersonalValue}", UriKind.Relative));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var correlationId = body.GetProperty(ErrorEnvelope.CorrelationIdMember).GetString()!;

        var failure = Assert.Single(sink.Events, e => e.MessageTemplate.Text.StartsWith("Request failed", StringComparison.Ordinal));
        Assert.Equal(LogEventLevel.Error, failure.Level);
        Assert.Equal(Quoted(correlationId), failure.Properties[LogPropertyNames.CorrelationId].ToString());
        Assert.Equal(Quoted(ReferenceCode.From(correlationId)), failure.Properties[LogPropertyNames.ReferenceCode].ToString());
        Assert.Equal(Quoted(ErrorCodes.InternalError), failure.Properties[LogPropertyNames.Code].ToString());
        Assert.Equal(Quoted(FailureClassifierTable.UnclassifiedName), failure.Properties[LogPropertyNames.Classifier].ToString());
        Assert.Equal(Quoted(TestPaths.Unclassified), failure.Properties[LogPropertyNames.RouteTemplate].ToString());
        Assert.True(failure.Properties.ContainsKey(LogPropertyNames.ErrorFingerprint));
        var expectedHash = app.Services.GetRequiredService<UserIdHash>().Of(ErrorHost.UserId);
        Assert.Equal(Quoted(expectedHash!), failure.Properties[LogPropertyNames.UserIdHash].ToString());
        Assert.NotNull(failure.Exception);

        // Logged ONCE: the request line records the 500 without the exception, and the framework adds no duplicate.
        Assert.Equal([failure], sink.Events.Where(e => e.Exception is not null));
        var requestLine = Assert.Single(sink.Events, e => e.Properties.ContainsKey("Elapsed"));
        Assert.Equal("500", requestLine.Properties[LogPropertyNames.StatusCode].ToString());
        Assert.Equal(Quoted(correlationId), requestLine.Properties[LogPropertyNames.CorrelationId].ToString());

        var everything = sink.AllText();
        Assert.DoesNotContain(TestPaths.PersonalValue, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(ErrorHost.UserId, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expected_failure_is_logged_below_error()
    {
        var (app, sink) = await ErrorHost.StartAsync();
        await using var _ = app;

        using var response = await app.GetTestClient().GetAsync(new Uri(TestPaths.RuleBroken, UriKind.Relative));

        var failure = Assert.Single(sink.Events, e => e.MessageTemplate.Text.StartsWith("Request failed", StringComparison.Ordinal));
        Assert.Equal(LogEventLevel.Information, failure.Level);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, (int)response.StatusCode);
    }

    [Fact]
    public async Task The_job_filter_resolves_codes_through_the_classifier_table()
    {
        var (app, _) = await ErrorHost.StartAsync();
        await using var _ = app;

        var resolver = app.Services.GetRequiredService<IFailureCodeResolver>();

        Assert.IsType<FailureClassifierTable>(resolver);
        Assert.Equal(ErrorCodes.Retryable, resolver.Resolve(new TimeoutException()).Code);
        Assert.Equal(ErrorCodes.BusinessRule, resolver.Resolve(new RuleBrokenException()).Code);
    }

    private static string Quoted(string value) => $"\"{value}\"";
}
