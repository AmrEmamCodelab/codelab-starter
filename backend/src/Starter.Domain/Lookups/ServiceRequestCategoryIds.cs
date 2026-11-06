namespace Starter.Domain.Lookups;

/// <summary>
/// The ids of the <see cref="LookupTypes.ServiceRequestCategories"/> lookup, exactly as its seed migration (AddServiceRequestCategoriesLookup)
/// writes them. Compare and assign these values only through these constants; never renumber one (stored data and
/// clients hold the ids). Written by tools/scaffold/new-lookup.mjs.
/// </summary>
public static class ServiceRequestCategoryIds
{
    /// <summary>General request (code <c>general</c>).</summary>
    public const int General = 1;

    /// <summary>Certificate request (code <c>certificate</c>).</summary>
    public const int Certificate = 2;

    /// <summary>Data update (code <c>data-update</c>).</summary>
    public const int DataUpdate = 3;

    /// <summary>Complaint (code <c>complaint</c>).</summary>
    public const int Complaint = 4;
}
