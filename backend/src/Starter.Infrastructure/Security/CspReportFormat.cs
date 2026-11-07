namespace Starter.Infrastructure.Security;

/// <summary>The two wire formats a browser uses to deliver a CSP violation report.</summary>
public enum CspReportFormat
{
    /// <summary>The CSP2 <c>report-uri</c> format (<c>application/csp-report</c>): one <c>{"csp-report": {...}}</c> object. Firefox and Safari.</summary>
    ReportUri = 1,

    /// <summary>The Reporting API format (<c>application/reports+json</c>): an array of reports of type <c>csp-violation</c>. Chromium.</summary>
    ReportingApi = 2,
}

/// <summary>Whether the violated policy was enforced or only reported.</summary>
public enum CspDisposition
{
    /// <summary>The report did not say (older browsers omit the field).</summary>
    Unspecified = 0,

    /// <summary>The policy blocked the resource.</summary>
    Enforce = 1,

    /// <summary>The policy is report-only: nothing was blocked.</summary>
    Report = 2,
}

/// <summary>The media types and limits of the CSP report sink.</summary>
public static class CspReportContract
{
    /// <summary>The <c>report-uri</c> media type.</summary>
    public const string ReportUriMediaType = "application/csp-report";

    /// <summary>The Reporting API media type.</summary>
    public const string ReportingApiMediaType = "application/reports+json";

    /// <summary>The largest request body accepted (16 KiB); a browser report is far below it.</summary>
    public const int MaxBodyBytes = 16 * 1024;

    /// <summary>The most violations logged from one Reporting API batch; the rest are dropped.</summary>
    public const int MaxViolationsPerRequest = 20;

    /// <summary>The longest value kept for one report field; longer values are cut.</summary>
    public const int MaxFieldLength = 512;

    /// <summary>The longest directive name kept.</summary>
    public const int MaxDirectiveLength = 64;

    /// <summary>The Reporting API report type of a CSP violation.</summary>
    public const string CspViolationReportType = "csp-violation";

    /// <summary>The directive logged when the report names none the sink recognises.</summary>
    public const string UnknownDirective = "unknown";

    /// <summary>The format of <paramref name="contentType"/> (parameters such as charset ignored), or null when the sink does not accept it.</summary>
    public static CspReportFormat? FormatOf(string? contentType)
    {
        if (!Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return null;
        }

        var mediaType = parsed.MediaType.Value;
        return string.Equals(mediaType, ReportUriMediaType, StringComparison.OrdinalIgnoreCase) ? CspReportFormat.ReportUri
            : string.Equals(mediaType, ReportingApiMediaType, StringComparison.OrdinalIgnoreCase) ? CspReportFormat.ReportingApi
            : null;
    }
}
