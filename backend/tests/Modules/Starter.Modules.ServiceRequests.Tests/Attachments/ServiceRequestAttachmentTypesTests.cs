using Codelab.Security.Uploads;
using Starter.Application.Attachments;
using Starter.Domain.Attachments;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;
using Starter.Modules.Attachments.Infrastructure.Startup;
using Starter.Modules.ServiceRequests.Application.Attachments;

namespace Starter.Modules.ServiceRequests.Tests.Attachments;

/// <summary>
/// The sample's attachment types, declared by the sample itself through the kernel seam <see cref="IAttachmentTypeSeedSource"/>
/// and written by the Attachments module at start: the contract values, their validation by the Attachments
/// declarations, and the upgrade from the retired seed migration (a database holding its rows is never seeded twice).
/// </summary>
public sealed class ServiceRequestAttachmentTypesTests
{
    [Fact]
    public void The_types_are_the_required_document_and_the_optional_supporting_files()
    {
        var types = new ServiceRequestAttachmentTypes().Types.ToList();

        Assert.Equal([AttachmentTypeIds.ServiceRequestDocument, AttachmentTypeIds.ServiceRequestSupporting], types.Select(type => type.Id));
        Assert.Equal([AttachmentTypeKeys.ServiceRequestDocument, AttachmentTypeKeys.ServiceRequestSupporting], types.Select(type => type.Key));
        Assert.All(types, type => Assert.Equal(AttachableRecordTypes.ServiceRequest, type.RecordType));
        Assert.Equal([(true, 3), (false, 5)], types.Select(type => (type.IsRequired, type.MaxFiles)));
        Assert.All(types, type => Assert.Equal([".pdf", ".jpg", ".png"], type.AllowedExtensions));
    }

    [Fact]
    public void The_ids_and_keys_are_the_shared_constants_and_never_the_demo_type()
    {
        Assert.Equal((2, "service-request-document"), (AttachmentTypeIds.ServiceRequestDocument, AttachmentTypeKeys.ServiceRequestDocument));
        Assert.Equal((3, "service-request-supporting"), (AttachmentTypeIds.ServiceRequestSupporting, AttachmentTypeKeys.ServiceRequestSupporting));
        Assert.DoesNotContain(AttachmentTypeIds.DemoDocument, ServiceRequestAttachmentTypes.All.Select(type => type.Id));
    }

    [Fact]
    public void The_attachments_module_accepts_the_declarations_and_they_fit_the_default_upload_allowlist()
    {
        var declarations = new AttachmentTypeDeclarations([new ServiceRequestAttachmentTypes()], AttachmentTypeSeed.Rows);

        Assert.Equal(2, declarations.Types.Count);
        Assert.All(declarations.Types, type => Assert.True(type.IsActive));
        AttachmentCatalogCheck.EnsureFits(declarations.Types, AttachmentCatalogCheck.AllowlistOf(new UploadSafetyOptions()));
    }

    [Fact]
    public void A_fresh_catalog_gets_both_types()
    {
        var plan = AttachmentTypeSeeding.Plan(AttachmentTypeSeed.Rows, Seeds());

        Assert.Equal([AttachmentTypeKeys.ServiceRequestDocument, AttachmentTypeKeys.ServiceRequestSupporting], plan.ToAdd.Select(type => type.Key));
    }

    [Fact]
    public void A_database_that_applied_the_retired_seed_migration_is_not_seeded_twice()
    {
        var stored = AttachmentTypeSeed.Rows.Concat(RetiredMigrationRows()).ToList();

        var plan = AttachmentTypeSeeding.Plan(stored, Seeds());

        Assert.True(plan.IsEmpty);
        Assert.Equal(2, plan.UnchangedCount);
    }

    [Fact]
    public void A_retired_sample_type_stays_retired_after_an_upgrade()
    {
        var stored = RetiredMigrationRows();
        stored[0].SetActive(false);

        var plan = AttachmentTypeSeeding.Plan(stored, Seeds());

        Assert.True(plan.IsEmpty);
        Assert.False(stored[0].IsActive);
    }

    private static List<ServiceAttachmentSeed> Seeds() =>
        [.. new AttachmentTypeDeclarations([new ServiceRequestAttachmentTypes()], AttachmentTypeSeed.Rows).Seeds];

    /// <summary>
    /// The two rows the Attachments migration 20261106150100_AddServiceRequestAttachmentTypes inserted before it was
    /// emptied, frozen here as data (literal values on purpose: they are what existing databases hold).
    /// </summary>
    private static List<ServiceAttachment> RetiredMigrationRows() =>
    [
        ServiceAttachment.Seeded(new ServiceAttachmentSeed(
            Id: 2,
            Key: "service-request-document",
            Names: new AttachmentTypeNames(Ar: "مستند الطلب", En: "Request document"),
            RecordType: "service-request",
            Rules: new AttachmentTypeRules(IsRequired: true, MaxFiles: 3, AllowedExtensions: [".pdf", ".jpg", ".png"]))),
        ServiceAttachment.Seeded(new ServiceAttachmentSeed(
            Id: 3,
            Key: "service-request-supporting",
            Names: new AttachmentTypeNames(Ar: "مستندات داعمة", En: "Supporting documents"),
            RecordType: "service-request",
            Rules: new AttachmentTypeRules(IsRequired: false, MaxFiles: 5, AllowedExtensions: [".pdf", ".jpg", ".png"]))),
    ];
}
