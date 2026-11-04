namespace Codelab.Errors;

/// <summary>The kind of an expected business "no" (<see cref="CodedRefusal"/>); each maps to one HTTP status.</summary>
public enum RefusalKind
{
    /// <summary>400: the input is invalid (a validator's refusal, with per-field errors).</summary>
    Invalid = 1,

    /// <summary>403: the caller may not do this to this resource.</summary>
    Forbidden = 2,

    /// <summary>404: the resource does not exist, or belongs to someone the caller may not see.</summary>
    NotFound = 3,

    /// <summary>409: the request conflicts with the resource's current state.</summary>
    Conflict = 4,

    /// <summary>422: understood, but a business rule says no.</summary>
    Unprocessable = 5,
}
