using System.Text.Json;

namespace Starter.Infrastructure.Security;

/// <summary>
/// Turns a browser's CSP report body into <see cref="CspViolation"/>s with one shape for both wire formats. Every URL is
/// redacted (no query, fragment or user info), every text field clipped, and only the fields the triage needs are
/// kept. A Reporting API batch keeps its <c>csp-violation</c> reports (other report types are skipped) up to
/// <see cref="CspReportContract.MaxViolationsPerRequest"/>.
/// </summary>
public static class CspReportNormalizer
{
    private const int MaxJsonDepth = 8;
    private const string ReportUriEnvelope = "csp-report";
    private const string ReportTypeMember = "type";
    private const string ReportBodyMember = "body";
    private const char DirectiveSeparator = ' ';

    private static readonly JsonDocumentOptions ParseOptions = new() { MaxDepth = MaxJsonDepth };

    private static readonly ReportFields ReportUriFields = new()
    {
        Document = "document-uri",
        Blocked = "blocked-uri",
        EffectiveDirective = "effective-directive",
        ViolatedDirective = "violated-directive",
        Disposition = "disposition",
        SourceFile = "source-file",
        Line = "line-number",
        Column = "column-number",
        Status = "status-code",
    };

    private static readonly ReportFields ReportingApiFields = new()
    {
        Document = "documentURL",
        Blocked = "blockedURL",
        EffectiveDirective = "effectiveDirective",
        ViolatedDirective = "violatedDirective",
        Disposition = "disposition",
        SourceFile = "sourceFile",
        Line = "lineNumber",
        Column = "columnNumber",
        Status = "statusCode",
    };

    /// <summary>
    /// Normalizes <paramref name="body"/> read as <paramref name="format"/>. False when the body is not JSON, is nested
    /// deeper than any report, or does not have the format's envelope; true (possibly with no violation) otherwise.
    /// </summary>
    public static bool TryNormalize(ReadOnlyMemory<byte> body, CspReportFormat format, out IReadOnlyList<CspViolation> violations)
    {
        violations = [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body, ParseOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var parsed = format == CspReportFormat.ReportUri ? FromReportUri(document.RootElement) : FromReportingApi(document.RootElement);
            violations = parsed ?? [];
            return parsed is not null;
        }
    }

    private static List<CspViolation>? FromReportUri(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(ReportUriEnvelope, out var report)
        && report.ValueKind == JsonValueKind.Object
            ? [Violation(report, ReportUriFields, CspReportFormat.ReportUri)]
            : null;

    private static List<CspViolation>? FromReportingApi(JsonElement root)
    {
        IEnumerable<JsonElement>? reports = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray(),
            JsonValueKind.Object => [root],
            _ => null,
        };
        return reports?
            .Where(IsCspViolation)
            .Take(CspReportContract.MaxViolationsPerRequest)
            .Select(report => Violation(report.GetProperty(ReportBodyMember), ReportingApiFields, CspReportFormat.ReportingApi))
            .ToList();
    }

    private static bool IsCspViolation(JsonElement report) =>
        report.ValueKind == JsonValueKind.Object
        && string.Equals(Text(report, ReportTypeMember), CspReportContract.CspViolationReportType, StringComparison.Ordinal)
        && report.TryGetProperty(ReportBodyMember, out var body)
        && body.ValueKind == JsonValueKind.Object;

    private static CspViolation Violation(JsonElement report, ReportFields fields, CspReportFormat format) => new()
    {
        Format = format,
        Directive = Directive(Text(report, fields.EffectiveDirective) ?? Text(report, fields.ViolatedDirective)),
        Disposition = Disposition(Text(report, fields.Disposition)),
        DocumentUri = CspReportUrls.Redact(Text(report, fields.Document)),
        BlockedUri = CspReportUrls.Redact(Text(report, fields.Blocked)),
        SourceFile = CspReportUrls.Redact(Text(report, fields.SourceFile)),
        LineNumber = Number(report, fields.Line),
        ColumnNumber = Number(report, fields.Column),
        StatusCode = Number(report, fields.Status),
    };

    /// <summary>The directive name: the first token (a <c>violated-directive</c> carries its source list), lowercase, letters, digits and hyphens only.</summary>
    private static string Directive(string? value)
    {
        var name = value?.Trim().Split(DirectiveSeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        return name is { Length: > 0 and <= CspReportContract.MaxDirectiveLength } && name.All(IsDirectiveCharacter)
            ? name
            : CspReportContract.UnknownDirective;
    }

    private static bool IsDirectiveCharacter(char character) => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-';

    private static CspDisposition Disposition(string? value) =>
        string.Equals(value, nameof(CspDisposition.Enforce), StringComparison.OrdinalIgnoreCase) ? CspDisposition.Enforce
        : string.Equals(value, nameof(CspDisposition.Report), StringComparison.OrdinalIgnoreCase) ? CspDisposition.Report
        : CspDisposition.Unspecified;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    /// <summary>The member names of one wire format.</summary>
    private sealed class ReportFields
    {
        public required string Document { get; init; }

        public required string Blocked { get; init; }

        public required string EffectiveDirective { get; init; }

        public required string ViolatedDirective { get; init; }

        public required string Disposition { get; init; }

        public required string SourceFile { get; init; }

        public required string Line { get; init; }

        public required string Column { get; init; }

        public required string Status { get; init; }
    }
}
