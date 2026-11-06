namespace Starter.Domain.Lookups;

/// <summary>
/// The ids of the <see cref="LookupTypes.ContactTopics"/> lookup, exactly as its seed migration (AddContactTopicsLookup)
/// writes them. Compare and assign these values only through these constants; never renumber one (stored data and
/// clients hold the ids). Written by tools/scaffold/new-lookup.mjs.
/// </summary>
public static class ContactTopicIds
{
    /// <summary>General question (code <c>general</c>).</summary>
    public const int General = 1;

    /// <summary>A request I submitted (code <c>request</c>).</summary>
    public const int Request = 2;

    /// <summary>My account and sign-in (code <c>account</c>).</summary>
    public const int Account = 3;

    /// <summary>Suggestion or feedback (code <c>suggestion</c>).</summary>
    public const int Suggestion = 4;
}
