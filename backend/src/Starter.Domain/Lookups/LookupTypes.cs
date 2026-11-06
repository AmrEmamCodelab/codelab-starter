namespace Starter.Domain.Lookups;

/// <summary>
/// The keys of the lookup types the migrations seed (kebab-case, the <c>{type}</c> segment of
/// <c>GET /api/lookups/{type}</c>). Compare and request lookup types only through these constants. Add one with
/// <c>node tools/scaffold/new-lookup.mjs --type &lt;kebab&gt; --name &lt;Pascal&gt; --codes &lt;a,b&gt;</c>.
/// </summary>
public static class LookupTypes
{
    /// <summary>The contact topics lookup (<see cref="ContactTopicIds"/>).</summary>
    public const string ContactTopics = "contact-topics";

    /// <summary>The service request categories lookup (<see cref="ServiceRequestCategoryIds"/>).</summary>
    public const string ServiceRequestCategories = "service-request-categories";

    // scaffold:lookup-types
}
