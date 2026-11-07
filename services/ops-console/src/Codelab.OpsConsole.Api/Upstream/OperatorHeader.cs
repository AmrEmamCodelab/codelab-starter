using Codelab.OpsConsole.Application.Ports;

namespace Codelab.OpsConsole.Api.Upstream;

/// <summary>
/// The header that names the operator on every <c>/api/ops</c> call. It travels on the same request as the service
/// assertion, so only a caller holding the console's ES256 key can send it to the API.
/// <para>
/// Encoding: a name of printable ASCII without <c>%</c> (the usual <c>DOMAIN\user</c>) is sent as is; any other name
/// is sent percent-encoded as UTF-8 (<see cref="Uri.EscapeDataString(string)"/>), so a receiver decodes a value that
/// contains <c>%</c> and reads every other value verbatim.
/// </para>
/// </summary>
public static class OperatorHeader
{
    /// <summary>The header name.</summary>
    public const string Name = "X-Codelab-Operator";

    private const char FirstPrintable = ' ';
    private const char LastPrintable = '~';
    private const char Escape = '%';

    /// <summary>The header value for <paramref name="operatorName"/>.</summary>
    public static string ValueFor(OperatorName operatorName)
    {
        var value = operatorName.Value;
        return value.All(IsSafe) ? value : Uri.EscapeDataString(value);
    }

    private static bool IsSafe(char character) => character is >= FirstPrintable and <= LastPrintable and not Escape;
}
