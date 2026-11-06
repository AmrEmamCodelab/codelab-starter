using Starter.Domain.Attachments;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Tests.Fakes;

namespace Starter.Modules.Attachments.Tests.Domain;

public sealed class ServiceAttachmentTests
{
    [Fact]
    public void A_seeded_type_keeps_its_values_and_normalises_the_extensions()
    {
        var type = ServiceAttachment.Seeded(new ServiceAttachmentSeed(
            Id: AttachmentTypeIds.DemoDocument,
            Key: AttachmentTypeKeys.DemoDocument,
            Names: new AttachmentTypeNames(Ar: "مستند", En: "Document"),
            RecordType: AttachableRecordTypes.Demo,
            Rules: new AttachmentTypeRules(IsRequired: true, MaxFiles: 5, AllowedExtensions: ["PDF", ".png", ".pdf"])));

        Assert.Equal((1, "demo-document", "attachments-demo", true, 5, true), (type.Id, type.Key, type.RecordType, type.IsRequired, type.MaxFiles, type.IsActive));
        Assert.Equal(("مستند", "Document"), (type.NameAr, type.NameEn));
        Assert.Equal([".pdf", ".png"], type.AllowedExtensions);
        Assert.Equal(".pdf;.png", type.AllowedExtensionsValue);
    }

    public static TheoryData<ServiceAttachmentSeed> BadSeeds => new()
    {
        Valid with { Id = 0 },
        Valid with { Key = "Not Kebab" },
        Valid with { RecordType = "bad_type" },
        Valid with { Rules = Valid.Rules with { MaxFiles = 0 } },
        Valid with { Rules = Valid.Rules with { MaxFiles = AttachmentsLimits.MaxFilesCeiling + 1 } },
        Valid with { Names = new AttachmentTypeNames(Ar: "ا", En: " ") },
        Valid with { Names = new AttachmentTypeNames(Ar: new string('x', AttachmentsLimits.NameMaxLength + 1), En: "A") },
        Valid with { Rules = Valid.Rules with { AllowedExtensions = [] } },
        Valid with { Rules = Valid.Rules with { AllowedExtensions = [".p df"] } },
    };

    [Theory]
    [MemberData(nameof(BadSeeds))]
    public void A_malformed_seed_is_refused(ServiceAttachmentSeed seed)
    {
        Assert.ThrowsAny<ArgumentException>(() => ServiceAttachment.Seeded(seed));
    }

    [Theory]
    [InlineData("scan.pdf", true)]
    [InlineData("SCAN.PDF", true)]
    [InlineData("photo.png. ", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("noextension", false)]
    [InlineData("a.pdf:evil.exe", false)]
    public void The_type_allows_a_file_by_its_normalised_extension(string fileName, bool allowed)
    {
        Assert.Equal(allowed, TestData.DemoType().AllowsFile(fileName));
    }

    [Fact]
    public void Room_and_remaining_slots_follow_max_files()
    {
        var type = TestData.DemoType(maxFiles: 2);

        Assert.True(type.HasRoomFor(1));
        Assert.False(type.HasRoomFor(2));
        Assert.Equal(1, type.RemainingSlots(1));
        Assert.Equal(0, type.RemainingSlots(3));
    }

    [Fact]
    public void Only_an_active_required_type_is_enforced()
    {
        Assert.True(TestData.DemoType().IsEnforcedAsRequired);
        Assert.False(TestData.DemoType(isActive: false).IsEnforcedAsRequired);
        Assert.False(TestData.OptionalType().IsEnforcedAsRequired);
    }

    [Fact]
    public void Set_active_reports_a_change_only_once()
    {
        var type = TestData.DemoType();

        Assert.False(type.SetActive(true));
        Assert.True(type.SetActive(false));
        Assert.False(type.IsActive);
    }

    private static ServiceAttachmentSeed Valid =>
        new(1, "a-type", new AttachmentTypeNames(Ar: "ا", En: "A"), "a-record", new AttachmentTypeRules(IsRequired: false, MaxFiles: 1, AllowedExtensions: [".pdf"]));
}

public sealed class AttachmentExtensionsTests
{
    [Theory]
    [InlineData("pdf", ".pdf")]
    [InlineData(".PNG", ".png")]
    [InlineData(" jpeg ", ".jpeg")]
    public void An_extension_is_normalised(string input, string expected)
    {
        Assert.Equal(expected, AttachmentExtensions.Normalize(input));
    }

    [Theory]
    [InlineData(".")]
    [InlineData(".p-f")]
    [InlineData(".abcdefghijklmnop")]
    [InlineData("  ")]
    public void A_malformed_extension_is_refused(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => AttachmentExtensions.Normalize(input));
    }

    [Fact]
    public void The_stored_form_round_trips_without_duplicates()
    {
        var stored = AttachmentExtensions.ToStorage(["PDF", ".pdf", "png"]);

        Assert.Equal(".pdf;.png", stored);
        Assert.Equal([".pdf", ".png"], AttachmentExtensions.FromStorage(stored));
        Assert.Empty(AttachmentExtensions.FromStorage(null));
    }

    [Fact]
    public void A_stored_form_longer_than_the_column_is_refused()
    {
        var many = Enumerable.Range(0, 40).Select(index => ".ext" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "abcdefg");

        Assert.Throws<ArgumentException>(() => AttachmentExtensions.ToStorage(many));
    }

    [Theory]
    [InlineData("Report.PDF", ".pdf")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("trailing.pdf...", ".pdf")]
    [InlineData("stream.pdf:x.exe", "")]
    [InlineData("none", "")]
    public void The_extension_of_a_file_name_is_lower_case_and_normalised(string fileName, string expected)
    {
        Assert.Equal(expected, AttachmentExtensions.Of(fileName));
    }
}

public sealed class AttachmentsLimitsTests
{
    [Theory]
    [InlineData("fileIdAlpha00000000001", true)]
    [InlineData("abc-DEF_123abc-DEF_123x", true)]
    [InlineData("short", false)]
    [InlineData("fileIdAlpha0000000000/", false)]
    [InlineData("fileIdAlpha0000000000=", false)]
    [InlineData(null, false)]
    public void A_file_id_is_url_safe_and_long_enough(string? fileId, bool expected)
    {
        Assert.Equal(expected, AttachmentsLimits.IsFileId(fileId));
    }

    [Fact]
    public void A_file_id_longer_than_the_maximum_is_refused()
    {
        Assert.False(AttachmentsLimits.IsFileId(new string('a', AttachmentsLimits.FileIdMaxLength + 1)));
    }

    [Theory]
    [InlineData(TestData.Sha256, true)]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [InlineData("0123", false)]
    [InlineData(null, false)]
    public void A_sha256_is_64_lower_case_hex_characters(string? value, bool expected)
    {
        Assert.Equal(expected, AttachmentsLimits.IsSha256Hex(value));
    }

    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("تقرير.pdf", true)]
    [InlineData("a\"b.pdf", false)]
    [InlineData("dir/a.pdf", false)]
    [InlineData("dir\\a.pdf", false)]
    [InlineData("a.pdf:x", false)]
    [InlineData("a\r\n.pdf", false)]
    [InlineData(" ", false)]
    public void A_storable_file_name_is_a_header_safe_leaf(string fileName, bool expected)
    {
        Assert.Equal(expected, AttachmentsLimits.IsStorableFileName(fileName));
    }

    [Fact]
    public void A_file_name_longer_than_the_column_is_not_storable()
    {
        Assert.False(AttachmentsLimits.IsStorableFileName(new string('a', AttachmentsLimits.FileNameMaxLength) + ".pdf"));
    }
}

public sealed class RecordAttachmentTests
{
    private static readonly AttachmentStamp Uploaded = new(TestData.User, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    [Fact]
    public void A_link_keeps_the_verified_file_and_the_uploader()
    {
        var owner = new AttachmentOwner(AttachableRecordTypes.Demo, RecordId: 7, ServiceAttachmentId: AttachmentTypeIds.DemoDocument);

        var link = RecordAttachment.Link(owner, TestData.File(), Uploaded);

        Assert.Equal((AttachableRecordTypes.Demo, 7L, 1), (link.RecordType, link.RecordId, link.ServiceAttachmentId));
        Assert.Equal(("fileIdAlpha00000000001", "report.pdf", 1024L, TestData.Sha256, "application/pdf"), (link.FileId, link.FileName, link.SizeBytes, link.Sha256Hex, link.ContentType));
        Assert.Equal((TestData.User, Uploaded.At), (link.UploadedBySubject, link.UploadedAt));
        Assert.True(link.IsLive);
        Assert.True(link.BelongsTo(owner));
        Assert.False(link.BelongsTo(owner with { RecordId = 8 }));
        Assert.False(link.BelongsTo(owner with { ServiceAttachmentId = 2 }));
    }

    public static TheoryData<StoredFile> BadFiles => new()
    {
        TestData.File() with { FileId = "../../etc/passwd" },
        TestData.File() with { FileName = "../x.pdf" },
        TestData.File() with { Sha256Hex = "abc" },
        TestData.File() with { ContentType = " " },
        TestData.File() with { SizeBytes = 0 },
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public void A_malformed_file_is_not_well_formed_and_cannot_be_linked(StoredFile file)
    {
        var owner = new AttachmentOwner(AttachableRecordTypes.Demo, RecordId: 1, ServiceAttachmentId: 1);

        Assert.False(file.IsWellFormed());
        Assert.ThrowsAny<ArgumentException>(() => RecordAttachment.Link(owner, file, Uploaded));
    }

    [Theory]
    [InlineData("Bad Type", 1L, 1)]
    [InlineData("attachments-demo", 0L, 1)]
    [InlineData("attachments-demo", 1L, 0)]
    public void A_malformed_owner_cannot_be_linked(string recordType, long recordId, int typeId)
    {
        var owner = new AttachmentOwner(recordType, recordId, typeId);

        Assert.ThrowsAny<ArgumentException>(() => RecordAttachment.Link(owner, TestData.File(), Uploaded));
    }

    [Fact]
    public void Removal_is_soft_and_keeps_the_first_removal()
    {
        var link = TestData.Link(TestData.DemoType(), "fileIdAlpha00000000001");
        var first = new AttachmentStamp(TestData.OtherUser, Uploaded.At.AddHours(1));

        Assert.True(link.Remove(first));
        Assert.False(link.Remove(new AttachmentStamp(TestData.User, Uploaded.At.AddHours(2))));
        Assert.False(link.IsLive);
        Assert.Equal((first.At, TestData.OtherUser), (link.RemovedAt!.Value, link.RemovedBySubject!.Value));
    }
}

public sealed class RequiredAttachmentRuleTests
{
    [Fact]
    public void Missing_keys_are_the_enforced_types_without_a_live_attachment_in_key_order()
    {
        var demo = TestData.DemoType();
        var optional = TestData.OptionalType(isRequired: true);

        Assert.Equal([AttachmentTypeKeys.DemoDocument, TestData.OptionalTypeKey], RequiredAttachmentRule.MissingTypeKeys([optional, demo], []));
        Assert.Equal([TestData.OptionalTypeKey], RequiredAttachmentRule.MissingTypeKeys([demo, optional], [TestData.Link(demo, "fileIdAlpha00000000001")]));
    }

    [Fact]
    public void A_removed_attachment_does_not_cover_its_type()
    {
        var demo = TestData.DemoType();
        var removed = TestData.Link(demo, "fileIdAlpha00000000001");
        _ = removed.Remove(new AttachmentStamp(TestData.User, DateTimeOffset.UnixEpoch));

        Assert.Equal([AttachmentTypeKeys.DemoDocument], RequiredAttachmentRule.MissingTypeKeys([demo], [removed]));
    }

    [Fact]
    public void Optional_and_retired_types_are_never_missing()
    {
        Assert.Empty(RequiredAttachmentRule.MissingTypeKeys([TestData.OptionalType(), TestData.DemoType(isActive: false)], []));
    }
}

public sealed class AttachmentTypeCatalogTests
{
    [Fact]
    public void A_catalog_inside_the_allowlist_has_no_violation()
    {
        var allowlist = new UploadAllowlist(Allowed: [".PDF", ".png", ".jpg"], Blocked: [".exe"]);

        Assert.Empty(AttachmentTypeCatalog.FindViolations([TestData.DemoType(), TestData.OptionalType()], allowlist));
    }

    [Fact]
    public void Extensions_outside_the_allowlist_or_blocked_are_reported_in_order()
    {
        var allowlist = new UploadAllowlist(Allowed: [".pdf", ".jpg"], Blocked: [".JPG"]);

        var violations = AttachmentTypeCatalog.FindViolations([TestData.OptionalType(), TestData.DemoType()], allowlist);

        Assert.Equal(
            [new AttachmentTypeViolation(AttachmentTypeKeys.DemoDocument, ".png"), new AttachmentTypeViolation(TestData.OptionalTypeKey, ".jpg")],
            violations);
    }
}
