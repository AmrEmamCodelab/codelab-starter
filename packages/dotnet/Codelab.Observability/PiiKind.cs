namespace Codelab.Observability;

/// <summary>How a <see cref="PiiAttribute"/> property is masked in logs (<see cref="PiiMask.For"/>). Stored values are explicit.</summary>
public enum PiiKind
{
    /// <summary>Nothing of the value is kept (a name, a national id, free text).</summary>
    Hidden = 1,

    /// <summary><see cref="PiiMask.Email"/>.</summary>
    Email = 2,

    /// <summary><see cref="PiiMask.Phone"/>.</summary>
    Phone = 3,

    /// <summary><see cref="PiiMask.LoginName"/>.</summary>
    LoginName = 4,

    /// <summary><see cref="PiiMask.UrlWithoutQuery"/>.</summary>
    Url = 5,

    /// <summary><see cref="PiiMask.Recipient"/>.</summary>
    Recipient = 6,
}
