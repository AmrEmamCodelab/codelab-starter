namespace Starter.Application.Users;

/// <summary>
/// Who is calling: the identity carried by the validated access token (the token holds identity only, never
/// permissions). Permissions are resolved live per request by the Access module.
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>True when the request carries a validated user token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The stable subject of the user (token <c>sub</c>), or null when anonymous or unreadable.</summary>
    Guid? SubjectId { get; }

    /// <summary>The session id (token <c>sid</c>), or null when anonymous.</summary>
    string? SessionId { get; }
}
