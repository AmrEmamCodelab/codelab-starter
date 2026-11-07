using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;

/// <summary>One lookup type and its values, as a migration inserts them.</summary>
/// <param name="TypeKey">The kebab-case type key (a <see cref="LookupTypes"/> constant).</param>
/// <param name="Names">The Arabic and English type names.</param>
/// <param name="Items">The values, with the fixed ids of the type's constants class.</param>
public sealed record LookupTypeSeed(string TypeKey, LookupNames Names, IReadOnlyList<LookupItemSeed> Items);

/// <summary>
/// The lookup types <see cref="InitialCreate"/> inserts: the seed migrations folded into it by the v1.0 squash
/// (ADR-010; once <c>AddContactTopicsLookup</c> and <c>AddServiceRequestCategoriesLookup</c>). Frozen with the
/// migration: a new type, a new value or changed wording is a NEW migration (written by
/// <c>tools/scaffold/new-lookup.mjs</c>), never an edit here.
/// </summary>
public static class InitialLookupTypes
{
    /// <summary>The portal contact form topics (<see cref="ContactTopicIds"/>).</summary>
    public static LookupTypeSeed ContactTopics { get; } = new(
        TypeKey: LookupTypes.ContactTopics,
        Names: new(Ar: "مواضيع التواصل", En: "Contact topics"),
        Items:
        [
            new(Id: ContactTopicIds.General, Code: "general", Names: new(Ar: "استفسار عام", En: "General question"), SortOrder: 10),
            new(Id: ContactTopicIds.Request, Code: "request", Names: new(Ar: "طلب قدّمته", En: "A request I submitted"), SortOrder: 20),
            new(Id: ContactTopicIds.Account, Code: "account", Names: new(Ar: "حسابي وتسجيل الدخول", En: "My account and sign-in"), SortOrder: 30),
            new(Id: ContactTopicIds.Suggestion, Code: "suggestion", Names: new(Ar: "اقتراح أو ملاحظة", En: "Suggestion or feedback"), SortOrder: 40),
        ]);

    /// <summary>
    /// The categories of the deletable ServiceRequests sample (<see cref="ServiceRequestCategoryIds"/>). The rows and
    /// the constants stay when the sample is removed; retire them with a new migration.
    /// </summary>
    public static LookupTypeSeed ServiceRequestCategories { get; } = new(
        TypeKey: LookupTypes.ServiceRequestCategories,
        Names: new(Ar: "تصنيفات طلبات الخدمة", En: "Service request categories"),
        Items:
        [
            new(Id: ServiceRequestCategoryIds.General, Code: "general", Names: new(Ar: "طلب عام", En: "General request"), SortOrder: 10),
            new(Id: ServiceRequestCategoryIds.Certificate, Code: "certificate", Names: new(Ar: "طلب شهادة", En: "Certificate request"), SortOrder: 20),
            new(Id: ServiceRequestCategoryIds.DataUpdate, Code: "data-update", Names: new(Ar: "تحديث بيانات", En: "Data update"), SortOrder: 30),
            new(Id: ServiceRequestCategoryIds.Complaint, Code: "complaint", Names: new(Ar: "شكوى", En: "Complaint"), SortOrder: 40),
        ]);

    /// <summary>Every type <see cref="InitialCreate"/> inserts, in insert order.</summary>
    public static IReadOnlyList<LookupTypeSeed> All { get; } = [ContactTopics, ServiceRequestCategories];
}
