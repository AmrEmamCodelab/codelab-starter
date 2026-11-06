using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;

/// <summary>
/// Seeds the <see cref="LookupTypes.ContactTopics"/> lookup and its values (<see cref="ContactTopicIds"/>). Written by
/// tools/scaffold/new-lookup.mjs. It adds rows only, so its target model is <see cref="LookupsMigrationModel"/>.
/// </summary>
[DbContext(typeof(LookupsDbContext))]
[Migration(MigrationId)]
public sealed class AddContactTopicsLookup : Migration
{
    /// <summary>The migration id (UTC timestamp and class name).</summary>
    public const string MigrationId = "20261106090100_AddContactTopicsLookup";

    private static readonly LookupNames TypeNames = new(Ar: "مواضيع التواصل", En: "Contact topics");

    private static readonly LookupItemSeed[] Items =
    [
        new(Id: ContactTopicIds.General, Code: "general", Names: new(Ar: "استفسار عام", En: "General question"), SortOrder: 10),
        new(Id: ContactTopicIds.Request, Code: "request", Names: new(Ar: "طلب قدّمته", En: "A request I submitted"), SortOrder: 20),
        new(Id: ContactTopicIds.Account, Code: "account", Names: new(Ar: "حسابي وتسجيل الدخول", En: "My account and sign-in"), SortOrder: 30),
        new(Id: ContactTopicIds.Suggestion, Code: "suggestion", Names: new(Ar: "اقتراح أو ملاحظة", En: "Suggestion or feedback"), SortOrder: 40),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        LookupSeed.InsertType(migrationBuilder, LookupTypes.ContactTopics, TypeNames, Items);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        LookupSeed.DeleteType(migrationBuilder, LookupTypes.ContactTopics, Items);

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => LookupsMigrationModel.Build(modelBuilder);
}
