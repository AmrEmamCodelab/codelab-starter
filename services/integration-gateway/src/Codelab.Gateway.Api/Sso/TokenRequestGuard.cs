using System.Text;
using Codelab.Integration.Gateway;

namespace Codelab.Gateway.Api.Sso;

/// <summary>
/// The checks a relayed token request passes before it leaves the DMZ. The form is passed through byte for byte, so
/// the guard only bounds it: 1 to <see cref="GatewayRelayLimits.MaxSsoTokenFormFields"/> fields, each with a name,
/// ASCII only (a form body is percent-encoded). It never decodes or logs a value: they are secrets.
/// </summary>
internal static class TokenRequestGuard
{
    private const char FieldSeparator = '&';
    private const char NameValueSeparator = '=';
    private const byte HighestAsciiByte = 0x7E;
    private const byte LowestPrintableByte = 0x20;

    /// <summary>The problem with <paramref name="form"/>, or null when it may be relayed.</summary>
    public static string? Problem(byte[] form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.Length == 0)
        {
            return "The token request form is empty.";
        }

        if (form.Any(value => value is < LowestPrintableByte or > HighestAsciiByte))
        {
            return "The token request form must be percent-encoded ASCII.";
        }

        var fields = Encoding.ASCII.GetString(form).Split(FieldSeparator);
        if (fields.Length > GatewayRelayLimits.MaxSsoTokenFormFields)
        {
            return $"The token request form has more than {GatewayRelayLimits.MaxSsoTokenFormFields} fields.";
        }

        return fields.Any(field => field.Split(NameValueSeparator)[0].Length == 0)
            ? "Every token request field needs a name."
            : null;
    }
}
