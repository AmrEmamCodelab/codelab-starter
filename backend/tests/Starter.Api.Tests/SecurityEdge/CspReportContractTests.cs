using Starter.Infrastructure.Security;

namespace Starter.Api.Tests.SecurityEdge;

public sealed class CspReportContractTests
{
    [Theory]
    [InlineData("application/csp-report", CspReportFormat.ReportUri)]
    [InlineData("Application/CSP-Report; charset=utf-8", CspReportFormat.ReportUri)]
    [InlineData("application/reports+json", CspReportFormat.ReportingApi)]
    [InlineData("application/reports+json;charset=UTF-8", CspReportFormat.ReportingApi)]
    public void FormatOf_recognises_the_two_report_media_types(string contentType, CspReportFormat expected) =>
        Assert.Equal(expected, CspReportContract.FormatOf(contentType));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("not a media type")]
    public void FormatOf_refuses_anything_else(string? contentType) => Assert.Null(CspReportContract.FormatOf(contentType));

    [Fact]
    public void The_body_ceiling_is_16_KiB() => Assert.Equal(16384, CspReportContract.MaxBodyBytes);
}
