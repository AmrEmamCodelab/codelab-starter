namespace Starter.Application.Results;

/// <summary>The kind of an expected business "no"; the host maps each to one HTTP status.</summary>
public enum RefusalCategory
{
    /// <summary>The input is invalid (400), with per-field errors or a module code.</summary>
    Invalid = 1,

    /// <summary>The caller may not do this to this resource (403).</summary>
    Forbidden = 2,

    /// <summary>The resource does not exist or is not visible to the caller (404).</summary>
    NotFound = 3,

    /// <summary>The request conflicts with the resource's current state (409).</summary>
    Conflict = 4,

    /// <summary>Understood, but a business rule says no (422).</summary>
    Unprocessable = 5,
}
