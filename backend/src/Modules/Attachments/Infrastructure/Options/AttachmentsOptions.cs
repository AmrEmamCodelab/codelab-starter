using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Modules.Attachments.Infrastructure.Storage;

namespace Starter.Modules.Attachments.Infrastructure.Options;

/// <summary>
/// Where the attachment service is and how long an upload ticket lives (section <c>Attachments</c>), validated on
/// start. The key material (ticket ring, receipt ring, service-assertion key) lives in the Codelab.Security sections,
/// never here.
/// </summary>
public sealed class AttachmentsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Attachments";

    /// <summary>The default upload ticket lifetime: five minutes, enough to pick and send a file.</summary>
    public const int DefaultTicketLifetimeSeconds = 300;

    /// <summary>The shortest upload ticket lifetime accepted.</summary>
    public const int MinTicketLifetimeSeconds = 30;

    /// <summary>The longest upload ticket lifetime accepted (a ticket is a bearer credential for one upload).</summary>
    public const int MaxTicketLifetimeSeconds = 3600;

    /// <summary>
    /// The attachment service root, absolute (Development <c>http://localhost:5185</c>). The API calls
    /// <c>&lt;root&gt;/api/files/{fileId}</c> server to server, and the browser uploads to <c>&lt;root&gt;/api/uploads</c>
    /// (the ticket's upload URL), so it must be the address the browser reaches too. HTTPS outside Development.
    /// Required: there is no default.
    /// </summary>
    public string? ServiceBaseUrl { get; set; }

    /// <summary>
    /// How long an upload ticket opens, in seconds (<see cref="MinTicketLifetimeSeconds"/> to
    /// <see cref="MaxTicketLifetimeSeconds"/>, and at most <c>Codelab:Security:UploadTickets:MaxLifetimeSeconds</c>).
    /// </summary>
    public int TicketLifetimeSeconds { get; set; } = DefaultTicketLifetimeSeconds;

    /// <summary>The ticket lifetime as a span.</summary>
    public TimeSpan TicketLifetime => TimeSpan.FromSeconds(TicketLifetimeSeconds);

    /// <summary>
    /// The service root as an absolute URI that ends with a slash, so relative paths resolve under it (a root with a
    /// path, such as a virtual directory, keeps that path).
    /// </summary>
    /// <exception cref="InvalidOperationException">The URL is missing or not absolute (the validator refuses the start first).</exception>
    public Uri ServiceRoot()
    {
        var text = ServiceBaseUrl?.Trim() ?? string.Empty;
        var withSlash = text.EndsWith(AttachmentServiceRoutes.PathSeparator) ? text : text + AttachmentServiceRoutes.PathSeparator;
        return Uri.TryCreate(withSlash, UriKind.Absolute, out var root)
            ? root
            : throw new InvalidOperationException($"{SectionName}:{nameof(ServiceBaseUrl)} is not an absolute URL.");
    }
}

/// <summary>
/// Validates <see cref="AttachmentsOptions"/> on start: an absolute http(s) service root without query or fragment
/// (HTTPS outside Development, because the browser sends the upload ticket there), and a ticket lifetime inside the
/// bounds and inside the upload-ticket ring's maximum.
/// </summary>
public sealed class AttachmentsOptionsValidator(IHostEnvironment environment, IOptions<UploadTicketRingOptions> tickets) : IValidateOptions<AttachmentsOptions>
{
    private const string Section = AttachmentsOptions.SectionName;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AttachmentsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        AddServiceUrlProblem(options.ServiceBaseUrl, problems);
        AddLifetimeProblem(options.TicketLifetimeSeconds, problems);
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private void AddServiceUrlProblem(string? serviceBaseUrl, List<string> problems)
    {
        const string key = $"{Section}:{nameof(AttachmentsOptions.ServiceBaseUrl)}";
        if (!Uri.TryCreate(serviceBaseUrl?.Trim(), UriKind.Absolute, out var root)
            || (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add($"{key} must be an absolute http or https URL (the attachment service root).");
            return;
        }

        if (!string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment))
        {
            problems.Add($"{key} must not carry a query or a fragment.");
        }
        else if (root.Scheme != Uri.UriSchemeHttps && !environment.IsDevelopment())
        {
            problems.Add($"{key} must be https outside Development: the browser sends the upload ticket to it.");
        }
    }

    private void AddLifetimeProblem(int lifetimeSeconds, List<string> problems)
    {
        const string key = $"{Section}:{nameof(AttachmentsOptions.TicketLifetimeSeconds)}";
        if (lifetimeSeconds is < AttachmentsOptions.MinTicketLifetimeSeconds or > AttachmentsOptions.MaxTicketLifetimeSeconds)
        {
            problems.Add($"{key} must be between {AttachmentsOptions.MinTicketLifetimeSeconds} and {AttachmentsOptions.MaxTicketLifetimeSeconds}.");
        }
        else if (lifetimeSeconds > tickets.Value.MaxLifetimeSeconds)
        {
            problems.Add($"{key} must not exceed {UploadTicketRingOptions.SectionName}:{nameof(UploadTicketRingOptions.MaxLifetimeSeconds)}.");
        }
    }
}
