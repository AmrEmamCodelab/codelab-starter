using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;

/// <summary>
/// Seeds the <see cref="LookupTypes.ServiceRequestCategories"/> lookup and its values (<see cref="ServiceRequestCategoryIds"/>). Written by
/// tools/scaffold/new-lookup.mjs. It adds rows only, so its target model is <see cref="LookupsMigrationModel"/>.
/// </summary>
[DbContext(typeof(LookupsDbContext))]
[Migration(MigrationId)]
public sealed class AddServiceRequestCategoriesLookup : Migration
{
    /// <summary>The migration id (UTC timestamp and class name).</summary>
    public const string MigrationId = "20261106090200_AddServiceRequestCategoriesLookup";

    private static readonly LookupNames TypeNames = new(Ar: "تصنيفات طلبات الخدمة", En: "Service request categories");

    private static readonly LookupItemSeed[] Items =
    [
        new(Id: ServiceRequestCategoryIds.General, Code: "general", Names: new(Ar: "طلب عام", En: "General request"), SortOrder: 10),
        new(Id: ServiceRequestCategoryIds.Certificate, Code: "certificate", Names: new(Ar: "طلب شهادة", En: "Certificate request"), SortOrder: 20),
        new(Id: ServiceRequestCategoryIds.DataUpdate, Code: "data-update", Names: new(Ar: "تحديث بيانات", En: "Data update"), SortOrder: 30),
        new(Id: ServiceRequestCategoryIds.Complaint, Code: "complaint", Names: new(Ar: "شكوى", En: "Complaint"), SortOrder: 40),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        LookupSeed.InsertType(migrationBuilder, LookupTypes.ServiceRequestCategories, TypeNames, Items);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        LookupSeed.DeleteType(migrationBuilder, LookupTypes.ServiceRequestCategories, Items);

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => LookupsMigrationModel.Build(modelBuilder);
}
