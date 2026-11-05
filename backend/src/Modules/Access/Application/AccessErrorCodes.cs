namespace Starter.Modules.Access.Application;

/// <summary>
/// The Access module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// </summary>
public static class AccessErrorCodes
{
    /// <summary>The access token carries no readable subject, so there is no user to resolve permissions for (403).</summary>
    public const string SubjectRequired = "ACCESS_SUBJECT_REQUIRED";
}
