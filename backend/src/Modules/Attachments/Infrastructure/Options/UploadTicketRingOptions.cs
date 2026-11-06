using System.Security.Cryptography;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Options;

namespace Starter.Modules.Attachments.Infrastructure.Options;

/// <summary>
/// The upload-ticket key ring (section <c>Codelab:Security:UploadTickets</c>), validated on start. It is the only ring
/// the attachment service holds (there it is the service's <c>Codelab:Security:Tickets</c>): the API seals upload
/// tickets with it and the service opens them. The API's own <c>Codelab:Security:Tickets</c> ring (SSO sign-in
/// cookies, SSO exchange tickets, account activation) never leaves the API, so a compromised service cannot open or
/// forge those tickets. Disabled until <see cref="Enabled"/> is set; then the ring is required and guarded like every
/// other ring.
/// </summary>
public sealed class UploadTicketRingOptions : KeyRingOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.Root + ":UploadTickets";

    /// <summary>Seal upload tickets with this ring (issuing an upload ticket fails while it is off).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The longest lifetime an upload ticket may be sealed with, in seconds (default
    /// <see cref="AttachmentsOptions.MaxTicketLifetimeSeconds"/>); <c>Attachments:TicketLifetimeSeconds</c> may not exceed it.
    /// </summary>
    public int MaxLifetimeSeconds { get; set; } = AttachmentsOptions.MaxTicketLifetimeSeconds;

    /// <summary>The ring as the <see cref="SealedTicketOptions"/> a <see cref="SealedTicketService"/> is built from.</summary>
    public SealedTicketOptions ToSealedTicketOptions() => new()
    {
        Enabled = Enabled,
        MaxLifetimeSeconds = MaxLifetimeSeconds,
        ActiveKeyId = ActiveKeyId,
        Keys = [.. Keys],
    };
}

/// <summary>
/// Validates <see cref="UploadTicketRingOptions"/> on start when enabled: a positive maximum lifetime, a guarded ring
/// (structure and the published-material denylist), and no key material shared with the API's own
/// <see cref="SealedTicketOptions.SectionName"/> ring (the two rings exist to be different).
/// </summary>
public sealed class UploadTicketRingOptionsValidator(KeyRingGuard guard, IOptions<SealedTicketOptions> apiTickets) : IValidateOptions<UploadTicketRingOptions>
{
    private const string Section = UploadTicketRingOptions.SectionName;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, UploadTicketRingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.MaxLifetimeSeconds <= 0)
        {
            return ValidateOptionsResult.Fail($"{Section}:{nameof(UploadTicketRingOptions.MaxLifetimeSeconds)} must be positive.");
        }

        var verdict = guard.Check(options, Section);
        if (!verdict.IsValid)
        {
            return ValidateOptionsResult.Fail(verdict.Error!);
        }

        return SharesMaterial(options, apiTickets.Value)
            ? ValidateOptionsResult.Fail($"{Section} shares key material with {SealedTicketOptions.SectionName}; the upload-ticket ring (the one the attachment service holds) must have its own keys.")
            : ValidateOptionsResult.Success;
    }

    private static bool SharesMaterial(KeyRingOptions uploads, KeyRingOptions api)
    {
        var apiMaterial = api.Keys.Select(key => Decoded(key.MaterialBase64)).OfType<byte[]>().ToList();
        return uploads.Keys
            .Select(key => Decoded(key.MaterialBase64))
            .OfType<byte[]>()
            .Any(material => apiMaterial.Exists(other => CryptographicOperations.FixedTimeEquals(material, other)));
    }

    private static byte[]? Decoded(string? base64)
    {
        var buffer = new byte[base64?.Length ?? 0];
        return Convert.TryFromBase64String(base64 ?? string.Empty, buffer, out var written) ? buffer[..written] : null;
    }
}
