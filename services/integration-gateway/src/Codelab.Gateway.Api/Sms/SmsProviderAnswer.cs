using System.Text.Json;
using Codelab.Gateway.Api.Relay;

namespace Codelab.Gateway.Api.Sms;

/// <summary>
/// Reads a provider's success answer. Without <see cref="SmsProviderOptions.AcceptedMember"/> and
/// <see cref="SmsProviderOptions.MessageIdMember"/> the body is not read: a 2xx answer means accepted. With them, the
/// body must be JSON: the accepted member must be a boolean and the message id a string or a number (absent: no id).
/// An answer that does not fit is <c>GATEWAY_UPSTREAM_REJECTED</c>: the gateway never guesses whether a message left.
/// </summary>
internal static class SmsProviderAnswer
{
    private const char PathSeparator = '.';

    public static SmsProviderOutcome Read(SmsProviderOptions options, UpstreamResult result)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            return new SmsProviderOutcome(Accepted: false, ProviderMessageId: null, result.Failure);
        }

        if (string.IsNullOrEmpty(options.AcceptedMember) && string.IsNullOrEmpty(options.MessageIdMember))
        {
            return new SmsProviderOutcome(Accepted: true, ProviderMessageId: null, Failure: null);
        }

        try
        {
            using var document = JsonDocument.Parse(result.Body);
            return ReadDocument(options, document.RootElement) ?? Unusable(result);
        }
        catch (JsonException)
        {
            return Unusable(result);
        }
    }

    private static SmsProviderOutcome? ReadDocument(SmsProviderOptions options, JsonElement root)
    {
        var accepted = true;
        if (!string.IsNullOrEmpty(options.AcceptedMember))
        {
            var member = Find(root, options.AcceptedMember);
            if (member is not { ValueKind: JsonValueKind.True or JsonValueKind.False })
            {
                return null;
            }

            accepted = member.Value.GetBoolean();
        }

        var messageId = string.IsNullOrEmpty(options.MessageIdMember) ? null : Find(root, options.MessageIdMember);
        return messageId switch
        {
            null or { ValueKind: JsonValueKind.Null } => new SmsProviderOutcome(accepted, ProviderMessageId: null, Failure: null),
            { ValueKind: JsonValueKind.String } => new SmsProviderOutcome(accepted, messageId.Value.GetString(), Failure: null),
            { ValueKind: JsonValueKind.Number } => new SmsProviderOutcome(accepted, messageId.Value.GetRawText(), Failure: null),
            _ => null,
        };
    }

    /// <summary>The member at the dotted <paramref name="path"/> below <paramref name="root"/>, or null.</summary>
    internal static JsonElement? Find(JsonElement root, string path)
    {
        var current = root;
        foreach (var name in path.Split(PathSeparator))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static SmsProviderOutcome Unusable(UpstreamResult result) =>
        new(Accepted: false, ProviderMessageId: null, UpstreamFailureMapper.Unusable(result.Status!.Value));
}
