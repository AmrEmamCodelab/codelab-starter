using System.Text;
using Starter.Infrastructure.Security;

namespace Starter.Api.Tests.SecurityEdge;

public sealed class CspReportNormalizerTests
{
    [Fact]
    public void A_report_uri_report_keeps_the_triage_fields_with_redacted_urls()
    {
        const string body = """
            {"csp-report":{"document-uri":"https://portal.example.test/a/b?id=7#x","blocked-uri":"https://evil.example.test/x.js?k=v",
            "effective-directive":"script-src-elem","disposition":"enforce","source-file":"https://portal.example.test/m.js?v=1",
            "line-number":3,"column-number":9,"status-code":200,"referrer":"https://r.example.test/?q=1","script-sample":"x"}}
            """;

        var violation = Assert.Single(Normalize(body, CspReportFormat.ReportUri));

        Assert.Equal(CspReportFormat.ReportUri, violation.Format);
        Assert.Equal("script-src-elem", violation.Directive);
        Assert.Equal(CspDisposition.Enforce, violation.Disposition);
        Assert.Equal("https://portal.example.test/a/b", violation.DocumentUri);
        Assert.Equal("https://evil.example.test/x.js", violation.BlockedUri);
        Assert.Equal("https://portal.example.test/m.js", violation.SourceFile);
        Assert.Equal(3, violation.LineNumber);
        Assert.Equal(9, violation.ColumnNumber);
        Assert.Equal(200, violation.StatusCode);
    }

    [Fact]
    public void A_report_uri_report_without_effective_directive_takes_the_first_token_of_the_violated_one()
    {
        const string body = """{"csp-report":{"violated-directive":"Style-Src 'self' https://x.example.test"}}""";

        var violation = Assert.Single(Normalize(body, CspReportFormat.ReportUri));

        Assert.Equal("style-src", violation.Directive);
        Assert.Equal(CspDisposition.Unspecified, violation.Disposition);
        Assert.Equal(string.Empty, violation.DocumentUri);
        Assert.Null(violation.LineNumber);
    }

    [Theory]
    [InlineData("script-src<script>")]
    [InlineData("")]
    public void A_directive_with_foreign_characters_is_unknown(string directive)
    {
        var body = "{\"csp-report\":{\"effective-directive\":\"" + directive + "\"}}";

        Assert.Equal(CspReportContract.UnknownDirective, Assert.Single(Normalize(body, CspReportFormat.ReportUri)).Directive);
    }

    [Fact]
    public void A_reporting_api_batch_keeps_csp_violations_and_skips_other_report_types()
    {
        const string body = """
            [{"type":"csp-violation","body":{"documentURL":"https://p.example.test/?a=1","blockedURL":"eval","effectiveDirective":"script-src","disposition":"report","lineNumber":4}},
             {"type":"deprecation","body":{"id":"x"}},
             {"type":"csp-violation","body":"not an object"}]
            """;

        var violation = Assert.Single(Normalize(body, CspReportFormat.ReportingApi));

        Assert.Equal(CspReportFormat.ReportingApi, violation.Format);
        Assert.Equal("script-src", violation.Directive);
        Assert.Equal(CspDisposition.Report, violation.Disposition);
        Assert.Equal("https://p.example.test/", violation.DocumentUri);
        Assert.Equal("eval", violation.BlockedUri);
        Assert.Equal(4, violation.LineNumber);
    }

    [Fact]
    public void A_single_reporting_api_object_is_accepted()
    {
        const string body = """{"type":"csp-violation","body":{"effectiveDirective":"img-src","blockedURL":"data:image/png;base64,AAAA"}}""";

        var violation = Assert.Single(Normalize(body, CspReportFormat.ReportingApi));

        Assert.Equal("data", violation.BlockedUri);
    }

    [Fact]
    public void A_reporting_api_batch_is_capped()
    {
        var report = """{"type":"csp-violation","body":{"effectiveDirective":"img-src"}}""";
        var body = "[" + string.Join(',', Enumerable.Repeat(report, CspReportContract.MaxViolationsPerRequest + 5)) + "]";

        Assert.Equal(CspReportContract.MaxViolationsPerRequest, Normalize(body, CspReportFormat.ReportingApi).Count);
    }

    [Theory]
    [InlineData("not json", CspReportFormat.ReportUri)]
    [InlineData("{\"other\":{}}", CspReportFormat.ReportUri)]
    [InlineData("{\"csp-report\":\"text\"}", CspReportFormat.ReportUri)]
    [InlineData("[]", CspReportFormat.ReportUri)]
    [InlineData("\"text\"", CspReportFormat.ReportingApi)]
    [InlineData("[[[[[[[[[[1]]]]]]]]]]", CspReportFormat.ReportingApi)]
    public void A_body_without_the_formats_envelope_is_refused(string body, CspReportFormat format)
    {
        Assert.False(CspReportNormalizer.TryNormalize(Encoding.UTF8.GetBytes(body), format, out var violations));
        Assert.Empty(violations);
    }

    [Fact]
    public void An_empty_reporting_api_batch_is_valid_and_has_no_violation()
    {
        Assert.True(CspReportNormalizer.TryNormalize(Encoding.UTF8.GetBytes("[]"), CspReportFormat.ReportingApi, out var violations));
        Assert.Empty(violations);
    }

    [Fact]
    public void A_non_integer_line_number_is_dropped()
    {
        const string body = """{"csp-report":{"line-number":"12","column-number":1.5}}""";

        var violation = Assert.Single(Normalize(body, CspReportFormat.ReportUri));

        Assert.Null(violation.LineNumber);
        Assert.Null(violation.ColumnNumber);
    }

    private static IReadOnlyList<CspViolation> Normalize(string body, CspReportFormat format)
    {
        Assert.True(CspReportNormalizer.TryNormalize(Encoding.UTF8.GetBytes(body), format, out var violations));
        return violations;
    }
}
