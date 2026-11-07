using Codelab.Observability;
using Microsoft.Extensions.Logging;

namespace Starter.Infrastructure.Security;

/// <summary>
/// The one structured log event of a CSP violation. Seq finds it by event name (<c>EventId.Name = 'CspViolation'</c>)
/// and groups it by the <c>Csp*</c> properties below; those names are the contract of the saved Seq queries, so a
/// rename is breaking.
/// </summary>
public static class CspViolationLog
{
    /// <summary>The logger category.</summary>
    public const string Category = "Starter.Security.CspReports";

    /// <summary>The event name.</summary>
    public const string EventName = "CspViolation";

    /// <summary>The event id (kernel security-edge range 1300-1399).</summary>
    public const int EventIdValue = 1301;

    /// <summary>The effective directive property.</summary>
    public const string DirectiveProperty = "CspDirective";

    /// <summary>The disposition property (Enforce or Report).</summary>
    public const string DispositionProperty = "CspDisposition";

    /// <summary>The redacted document URL property.</summary>
    public const string DocumentUriProperty = "CspDocumentUri";

    /// <summary>The redacted blocked resource property.</summary>
    public const string BlockedUriProperty = "CspBlockedUri";

    /// <summary>The redacted source file property.</summary>
    public const string SourceFileProperty = "CspSourceFile";

    /// <summary>The source line property.</summary>
    public const string LineNumberProperty = "CspLineNumber";

    /// <summary>The source column property.</summary>
    public const string ColumnNumberProperty = "CspColumnNumber";

    /// <summary>The document status property.</summary>
    public const string StatusCodeProperty = "CspStatusCode";

    /// <summary>The wire format property.</summary>
    public const string FormatProperty = "CspReportFormat";

    /// <summary>The event id with its name.</summary>
    public static readonly EventId Event = new(EventIdValue, EventName);

    private const string Template =
        "CSP violation of {" + DirectiveProperty + "} ({" + DispositionProperty + "}): {" + BlockedUriProperty + "} on {" + DocumentUriProperty + "}";

    /// <summary>Writes <paramref name="violation"/> as one Warning event.</summary>
    public static void Write(ILogger logger, CspViolation violation)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(violation);
        var state = new StructuredLogState(Template,
        [
            KeyValuePair.Create(DirectiveProperty, (object?)violation.Directive),
            KeyValuePair.Create(DispositionProperty, (object?)violation.Disposition),
            KeyValuePair.Create(BlockedUriProperty, (object?)violation.BlockedUri),
            KeyValuePair.Create(DocumentUriProperty, (object?)violation.DocumentUri),
            KeyValuePair.Create(SourceFileProperty, (object?)violation.SourceFile),
            KeyValuePair.Create(LineNumberProperty, (object?)violation.LineNumber),
            KeyValuePair.Create(ColumnNumberProperty, (object?)violation.ColumnNumber),
            KeyValuePair.Create(StatusCodeProperty, (object?)violation.StatusCode),
            KeyValuePair.Create(FormatProperty, (object?)violation.Format),
        ]);
        logger.Log(LogLevel.Warning, Event, state, exception: null, StructuredLogState.Format);
    }
}
