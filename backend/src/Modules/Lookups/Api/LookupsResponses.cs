using Codelab.Security.PublicIds;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Api;

/// <summary>One active lookup value of <c>GET /api/lookups/{type}</c>.</summary>
/// <param name="Id">The value id (opaque token, family <c>lookup-items</c>).</param>
/// <param name="Code">The kebab-case code.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="SortOrder">The display order.</param>
public sealed record LookupValueResponse(PublicId<LookupItemIds> Id, string Code, string NameAr, string NameEn, int SortOrder)
{
    /// <summary>The wire list of <paramref name="values"/>, in their order.</summary>
    public static IReadOnlyList<LookupValueResponse> ListFrom(IReadOnlyList<LookupValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return [.. values.Select(value => new LookupValueResponse(
            PublicId<LookupItemIds>.From(value.Id), value.Code, value.NameAr, value.NameEn, value.SortOrder))];
    }
}

/// <summary>One lookup type of <c>GET /api/admin/lookups</c>.</summary>
/// <param name="Type">The type key (the <c>{type}</c> route segment).</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="ItemCount">The number of its values, active or not.</param>
public sealed record LookupTypeResponse(string Type, string NameAr, string NameEn, int ItemCount)
{
    /// <summary>The wire list of <paramref name="types"/>, in their order.</summary>
    public static IReadOnlyList<LookupTypeResponse> ListFrom(IReadOnlyList<LookupTypeSummary> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return [.. types.Select(type => new LookupTypeResponse(type.Key, type.NameAr, type.NameEn, type.ItemCount))];
    }
}

/// <summary>One value of <c>GET /api/admin/lookups/{type}</c>, active or not.</summary>
/// <param name="Id">The value id (opaque token, family <c>lookup-items</c>).</param>
/// <param name="Code">The kebab-case code.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="SortOrder">The display order.</param>
/// <param name="IsActive">Whether the value is offered.</param>
public sealed record LookupItemResponse(
    PublicId<LookupItemIds> Id, string Code, string NameAr, string NameEn, int SortOrder, bool IsActive)
{
    /// <summary>The wire list of <paramref name="items"/>, in their order.</summary>
    public static IReadOnlyList<LookupItemResponse> ListFrom(IReadOnlyList<LookupItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return [.. items.Select(item => new LookupItemResponse(
            PublicId<LookupItemIds>.From(item.Id), item.Code, item.NameAr, item.NameEn, item.SortOrder, item.IsActive))];
    }
}
