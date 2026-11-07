using System.Text.Json.Serialization;

namespace Codelab.Integration.Gateway;

/// <summary>
/// The body of POST <see cref="GatewayRoutes.Sms"/>: <c>{"to":"...","text":"...","language":1}</c>. The language is an
/// integer (<see cref="GatewayLanguages"/>). Check it with <see cref="SmsRelayRequestValidator"/>.
/// </summary>
public sealed class SmsRelayRequest
{
    /// <summary>The recipient's mobile number, as the provider expects it.</summary>
    public string To { get; init; } = string.Empty;

    /// <summary>The message text, already rendered in <see cref="Language"/>.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The language of <see cref="Text"/> (<see cref="GatewayLanguages"/>).</summary>
    public int Language { get; init; }
}

/// <summary>
/// The answer of POST <see cref="GatewayRoutes.Sms"/> (200):
/// <c>{"accepted":true,"providerMessageId":"...","referenceCode":"..."}</c>; absent members are omitted.
/// </summary>
public sealed class SmsRelayResult
{
    /// <summary>True when the provider accepted the message for delivery.</summary>
    public bool Accepted { get; init; }

    /// <summary>The provider's message id, when it returns one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderMessageId { get; init; }

    /// <summary>The gateway's reference code for this relay, to find it in the gateway's logs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReferenceCode { get; init; }
}

/// <summary>The checks both sides run on an <see cref="SmsRelayRequest"/>: the client before sending, the gateway on receipt.</summary>
public static class SmsRelayRequestValidator
{
    /// <summary>Every problem with <paramref name="request"/>, empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(SmsRelayRequest? request)
    {
        if (request is null)
        {
            return ["The SMS relay request is required."];
        }

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(request.To) || request.To.Length > GatewayRelayLimits.MaxSmsRecipientLength || request.To.Any(char.IsControl))
        {
            problems.Add($"to is required, has no control characters and is at most {GatewayRelayLimits.MaxSmsRecipientLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > GatewayRelayLimits.MaxSmsTextLength)
        {
            problems.Add($"text is required and is at most {GatewayRelayLimits.MaxSmsTextLength} characters.");
        }

        if (!GatewayLanguages.IsKnown(request.Language))
        {
            problems.Add($"language must be {GatewayLanguages.Arabic} (Arabic) or {GatewayLanguages.English} (English).");
        }

        return problems;
    }
}
