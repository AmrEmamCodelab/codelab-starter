using System.Text.Json;

namespace Starter.Application.Validation;

/// <summary>
/// The wire name of a request field, derived from the member name (<c>FieldNames.Of(nameof(LoginCommand.UserName))</c>
/// gives <c>userName</c>), so the field errors use the names the client sent and a member rename moves them too.
/// </summary>
public static class FieldNames
{
    /// <summary>The camelCase wire name of <paramref name="memberName"/>.</summary>
    public static string Of(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        return JsonNamingPolicy.CamelCase.ConvertName(memberName);
    }
}
