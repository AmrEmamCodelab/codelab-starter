using Microsoft.Extensions.Options;
using Starter.Domain.Localization;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Infrastructure.Options;

/// <summary>The contact form's inbox (section <c>Portal:Contact</c>), validated on start.</summary>
public sealed class PortalContactOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Portal:Contact";

    /// <summary>
    /// The address every new contact message is announced to (required). The starter's default is a reserved,
    /// undeliverable placeholder; every deployment sets its own team inbox.
    /// </summary>
    public string InboxEmail { get; set; } = string.Empty;

    /// <summary>The language of the announcement e-mail, as the integer value (default Arabic, 1).</summary>
    public AppLanguage InboxLanguage { get; set; } = AppLanguage.Arabic;
}

/// <summary>Refuses a missing or malformed inbox address and an undefined language at start.</summary>
public sealed class PortalContactOptionsValidator : IValidateOptions<PortalContactOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PortalContactOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var inbox = options.InboxEmail?.Trim();
        if (string.IsNullOrEmpty(inbox) || inbox.Length > PortalLimits.EmailMaxLength || !EmailShape.IsWellFormed(inbox))
        {
            failures.Add($"{PortalContactOptions.SectionName}:{nameof(PortalContactOptions.InboxEmail)} must be one e-mail address of at most {PortalLimits.EmailMaxLength} characters.");
        }

        if (!Enum.IsDefined(options.InboxLanguage))
        {
            failures.Add($"{PortalContactOptions.SectionName}:{nameof(PortalContactOptions.InboxLanguage)} must be 1 (Arabic) or 2 (English).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
