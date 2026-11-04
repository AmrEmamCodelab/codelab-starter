using Microsoft.Extensions.Options;

namespace Codelab.Security;

/// <summary>
/// The one setting every wire name derives from (see <see cref="SecurityProtocol"/>). Required: there is no built-in
/// default, so a fork that forgot to set its prefix fails at start instead of speaking another product's protocol.
/// </summary>
public sealed class SecurityProtocolOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.Protocol;

    /// <summary>The product prefix, e.g. <c>CODELABTEST</c> (upper-case ASCII letters and digits, 2-16 characters).</summary>
    public string? Prefix { get; set; }

    /// <summary>The protocol for the configured prefix; throws when the options are invalid.</summary>
    public SecurityProtocol ToProtocol() => SecurityProtocol.Create(Prefix ?? string.Empty);
}

/// <summary>Validates <see cref="SecurityProtocolOptions"/> at start.</summary>
internal sealed class SecurityProtocolOptionsValidator : IValidateOptions<SecurityProtocolOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityProtocolOptions options) =>
        SecurityProtocol.IsValidPrefix(options.Prefix)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{SecurityProtocolOptions.SectionName}:{nameof(SecurityProtocolOptions.Prefix)} is required: {SecurityProtocol.MinPrefixLength}-{SecurityProtocol.MaxPrefixLength} characters, an upper-case ASCII letter first, then upper-case letters or digits (init.ps1 sets it per project).");
}
