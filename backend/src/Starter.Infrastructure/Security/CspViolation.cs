namespace Starter.Infrastructure.Security;

/// <summary>
/// One CSP violation after normalization: the same fields whichever format the browser used, every URL redacted by
/// <see cref="CspReportUrls"/>, every text field clipped. It never holds the referrer, the script sample or the
/// original policy (content, or noise that the deployed header already records).
/// </summary>
public sealed record CspViolation
{
    /// <summary>The wire format the report arrived in.</summary>
    public required CspReportFormat Format { get; init; }

    /// <summary>The effective directive (<c>script-src-elem</c>, <c>style-src</c>, ...), lowercase; <see cref="CspReportContract.UnknownDirective"/> when absent.</summary>
    public required string Directive { get; init; }

    /// <summary>Enforced or report-only.</summary>
    public required CspDisposition Disposition { get; init; }

    /// <summary>The redacted URL of the page that violated the policy.</summary>
    public required string DocumentUri { get; init; }

    /// <summary>The redacted blocked resource (a URL without query, a scheme such as <c>data</c>, or a keyword such as <c>inline</c>).</summary>
    public required string BlockedUri { get; init; }

    /// <summary>The redacted script or style file that caused the violation; empty when absent.</summary>
    public required string SourceFile { get; init; }

    /// <summary>The line in <see cref="SourceFile"/>, when given.</summary>
    public int? LineNumber { get; init; }

    /// <summary>The column in <see cref="SourceFile"/>, when given.</summary>
    public int? ColumnNumber { get; init; }

    /// <summary>The HTTP status of the document, when given.</summary>
    public int? StatusCode { get; init; }
}
