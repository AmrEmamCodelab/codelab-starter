using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Tests.Domain;

public sealed class AttachmentTypeSeedingTests
{
    private const string RecordType = "seeded-records";
    private const string OtherRecordType = "other-records";
    private const int FormId = 40;
    private const int PhotoId = 41;
    private const string FormKey = "seeded-form";
    private const string PhotoKey = "seeded-photo";

    private static ServiceAttachmentSeed Form { get; } = Seed(FormId, FormKey);

    private static ServiceAttachmentSeed Photo { get; } = Seed(PhotoId, PhotoKey);

    [Fact]
    public void An_empty_catalog_adds_every_declared_type()
    {
        var plan = AttachmentTypeSeeding.Plan([], [Form, Photo]);

        Assert.Equal([FormKey, PhotoKey], plan.ToAdd.Select(type => type.Key));
        Assert.Empty(plan.ToRefresh);
        Assert.Equal(0, plan.UnchangedCount);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void Rows_already_stored_as_declared_are_never_seeded_twice()
    {
        var stored = new[] { ServiceAttachment.Seeded(Form), ServiceAttachment.Seeded(Photo) };

        var plan = AttachmentTypeSeeding.Plan(stored, [Form, Photo]);

        Assert.True(plan.IsEmpty);
        Assert.Equal(2, plan.UnchangedCount);
    }

    [Fact]
    public void Planning_again_after_applying_a_plan_has_nothing_to_do()
    {
        var stored = new List<ServiceAttachment> { ServiceAttachment.Seeded(Form with { Names = new AttachmentTypeNames(Ar: "قديم", En: "Old") }) };
        var first = AttachmentTypeSeeding.Plan(stored, [Form, Photo]);
        stored.AddRange(first.ToAdd);
        Assert.Equal(1, first.ApplyRefreshes());

        var second = AttachmentTypeSeeding.Plan(stored, [Form, Photo]);

        Assert.True(second.IsEmpty);
        Assert.Equal(2, second.UnchangedCount);
    }

    [Fact]
    public void Changed_names_and_rules_are_refreshed_in_place()
    {
        var stored = ServiceAttachment.Seeded(Form);
        var changed = Form with
        {
            Names = new AttachmentTypeNames(Ar: "نموذج جديد", En: "New form"),
            Rules = new AttachmentTypeRules(IsRequired: false, MaxFiles: 7, AllowedExtensions: [".png"]),
        };

        var plan = AttachmentTypeSeeding.Plan([stored], [changed]);
        var refreshed = plan.ApplyRefreshes();

        Assert.Same(stored, Assert.Single(plan.ToRefresh).Stored);
        Assert.Equal(1, refreshed);
        Assert.Equal(("نموذج جديد", "New form", false, 7), (stored.NameAr, stored.NameEn, stored.IsRequired, stored.MaxFiles));
        Assert.Equal([".png"], stored.AllowedExtensions);
    }

    [Fact]
    public void A_retired_type_stays_retired_when_refreshed()
    {
        var stored = ServiceAttachment.Seeded(Form);
        stored.SetActive(false);

        var changed = stored.Refresh(Form with { Names = new AttachmentTypeNames(Ar: "آخر", En: "Other") });

        Assert.True(changed);
        Assert.False(stored.IsActive);
        Assert.Empty(AttachmentTypeSeeding.Plan([stored], [Form with { Names = new AttachmentTypeNames(Ar: "آخر", En: "Other") }]).ToAdd);
    }

    [Fact]
    public void A_refresh_with_the_same_content_reports_no_change()
    {
        var stored = ServiceAttachment.Seeded(Form);

        Assert.False(stored.Refresh(Form));
    }

    [Fact]
    public void A_refresh_never_moves_a_type_to_another_identity()
    {
        var stored = ServiceAttachment.Seeded(Form);

        Assert.Throws<ArgumentException>(() => stored.Refresh(Form with { Id = PhotoId }));
        Assert.Throws<ArgumentException>(() => stored.Refresh(Form with { Key = PhotoKey }));
        Assert.Throws<ArgumentException>(() => stored.Refresh(Form with { RecordType = OtherRecordType }));
    }

    [Fact]
    public void A_key_stored_under_another_id_is_a_conflict()
    {
        var stored = ServiceAttachment.Seeded(Form with { Id = PhotoId });

        var refused = Assert.Throws<InvalidOperationException>(() => AttachmentTypeSeeding.Plan([stored], [Form]));

        Assert.Contains(FormKey, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_stored_under_another_record_type_is_a_conflict()
    {
        var stored = ServiceAttachment.Seeded(Form with { RecordType = OtherRecordType });

        Assert.Throws<InvalidOperationException>(() => AttachmentTypeSeeding.Plan([stored], [Form]));
    }

    [Fact]
    public void An_id_held_by_another_key_is_a_conflict()
    {
        var stored = ServiceAttachment.Seeded(Photo with { Id = FormId });

        var refused = Assert.Throws<InvalidOperationException>(() => AttachmentTypeSeeding.Plan([stored], [Form]));

        Assert.Contains(PhotoKey, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stored_types_nobody_declares_are_left_alone()
    {
        var foreign = ServiceAttachment.Seeded(Photo);

        var plan = AttachmentTypeSeeding.Plan([foreign], [Form]);

        Assert.Equal([FormKey], plan.ToAdd.Select(type => type.Key));
        Assert.Empty(plan.ToRefresh);
        Assert.True(foreign.IsActive);
    }

    [Fact]
    public void A_malformed_declaration_is_refused()
    {
        Assert.Throws<ArgumentException>(() => AttachmentTypeSeeding.Plan([], [Form with { Key = "Not Kebab" }]));
    }

    private static ServiceAttachmentSeed Seed(int id, string key) => new(
        Id: id,
        Key: key,
        Names: new AttachmentTypeNames(Ar: "نموذج", En: "Form"),
        RecordType: RecordType,
        Rules: new AttachmentTypeRules(IsRequired: true, MaxFiles: 3, AllowedExtensions: [".pdf", ".jpg"]));
}
