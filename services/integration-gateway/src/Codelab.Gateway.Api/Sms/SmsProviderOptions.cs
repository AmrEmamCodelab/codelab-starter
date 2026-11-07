using Codelab.Gateway.Api.Relay;

namespace Codelab.Gateway.Api.Sms;

/// <summary>The HTTP method the SMS provider expects. Stored and bound as its integer value.</summary>
internal enum SmsProviderMethod
{
    /// <summary>Everything travels in the URL (<see cref="SmsProviderOptions.UrlTemplate"/>); no body.</summary>
    Get = 1,

    /// <summary>The URL plus a body rendered from <see cref="SmsProviderOptions.BodyTemplate"/>.</summary>
    Post = 2,
}

/// <summary>How a POST body is encoded. Stored and bound as its integer value.</summary>
internal enum SmsBodyFormat
{
    /// <summary><c>application/json</c>: placeholder values are JSON-escaped (write them inside quotes in the template).</summary>
    Json = 1,

    /// <summary><c>application/x-www-form-urlencoded</c>: placeholder values are percent-encoded.</summary>
    Form = 2,
}

/// <summary>
/// The SMS relay profile's upstream (<see cref="SectionName"/>): one generic HTTP provider described by templates, so
/// no provider is named in code. Placeholders (<see cref="SmsTemplate"/>): <c>{to}</c>, <c>{text}</c>,
/// <c>{language}</c> (the provider's code from <see cref="LanguageCodes"/>) and <c>{sender}</c>. Bound and validated
/// at start only when the profile is enabled.
/// </summary>
internal sealed class SmsProviderOptions : UpstreamOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Gateway:Sms";

    /// <summary>The provider URL with placeholders; values are percent-encoded. Absolute, https outside Development.</summary>
    public string? UrlTemplate { get; set; }

    /// <summary>The HTTP method.</summary>
    public SmsProviderMethod Method { get; set; } = SmsProviderMethod.Post;

    /// <summary>The POST body with placeholders (required for POST, empty for GET).</summary>
    public string? BodyTemplate { get; set; }

    /// <summary>The POST body encoding.</summary>
    public SmsBodyFormat BodyFormat { get; set; } = SmsBodyFormat.Json;

    /// <summary>The header that carries the provider credential (for example <c>Authorization</c>); empty for none.</summary>
    public string? AuthHeaderName { get; set; }

    /// <summary>The credential header's value. A secret: environment or secret store only, never committed or logged.</summary>
    public string? AuthHeaderValue { get; set; }

    /// <summary>The sender id the provider registered for the client, for <c>{sender}</c>.</summary>
    public string? Sender { get; set; }

    /// <summary>The provider's codes of the two message languages, for <c>{language}</c>.</summary>
    public SmsLanguageCodes LanguageCodes { get; set; } = new();

    /// <summary>
    /// Optional dotted path of a boolean member of the provider's JSON answer that says whether it accepted the
    /// message. Empty: any 2xx answer means accepted.
    /// </summary>
    public string? AcceptedMember { get; set; }

    /// <summary>Optional dotted path of the provider's message id (a string or a number) in its JSON answer.</summary>
    public string? MessageIdMember { get; set; }
}

/// <summary>The provider's language codes.</summary>
internal sealed class SmsLanguageCodes
{
    /// <summary>The code of an Arabic message.</summary>
    public string Arabic { get; set; } = SmsProviderDefaults.ArabicCode;

    /// <summary>The code of an English message.</summary>
    public string English { get; set; } = SmsProviderDefaults.EnglishCode;
}

/// <summary>Defaults and bounds of <see cref="SmsProviderOptions"/>.</summary>
internal static class SmsProviderDefaults
{
    /// <summary>The default code of an Arabic message.</summary>
    public const string ArabicCode = "ar";

    /// <summary>The default code of an English message.</summary>
    public const string EnglishCode = "en";

    /// <summary>The largest provider answer the gateway reads, in bytes (64 KiB).</summary>
    public const int MaxResponseBytes = 64 * 1024;
}
