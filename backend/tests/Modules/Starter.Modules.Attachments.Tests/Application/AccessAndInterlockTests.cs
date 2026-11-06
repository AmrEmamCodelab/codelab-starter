using Microsoft.Extensions.Time.Testing;
using Starter.Application.Attachments;
using Starter.Application.Results;
using Starter.Domain.Attachments;
using Starter.Domain.Exceptions;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Tests.Fakes;

namespace Starter.Modules.Attachments.Tests.Application;

public sealed class AttachableRecordsTests
{
    [Fact]
    public async Task An_allowed_record_has_no_refusal_and_asks_the_right_question()
    {
        var access = new FakeRecordAccess();
        var records = new AttachableRecords([access]);

        Assert.Null(await records.RefuseAsync(TestData.Demo, RecordAccessKind.View, CancellationToken.None));
        Assert.Null(await records.RefuseAsync(TestData.Demo, RecordAccessKind.Change, CancellationToken.None));
        Assert.Equal([(RecordAccessKind.View, TestData.RecordId), (RecordAccessKind.Change, TestData.RecordId)], access.Asked);
    }

    [Theory]
    [InlineData(true, false, RecordAccessKind.Change)]
    [InlineData(false, true, RecordAccessKind.View)]
    public async Task A_denied_record_answers_not_found(bool canView, bool canChange, RecordAccessKind kind)
    {
        var records = new AttachableRecords([new FakeRecordAccess { CanView = canView, CanChange = canChange }]);

        var refusal = await records.RefuseAsync(TestData.Demo, kind, CancellationToken.None);

        Assert.Equal((RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound), (refusal!.Category, refusal.Code));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public async Task A_record_id_below_one_is_not_found_without_asking(long recordId)
    {
        var access = new FakeRecordAccess();
        var records = new AttachableRecords([access]);

        var refusal = await records.RefuseAsync(new AttachableRecord(AttachableRecordTypes.Demo, recordId), RecordAccessKind.View, CancellationToken.None);

        Assert.Equal(AttachmentsErrorCodes.NotFound, refusal!.Code);
        Assert.Empty(access.Asked);
    }

    [Fact]
    public void Two_accesses_for_one_record_type_are_a_wiring_error()
    {
        Assert.Throws<InvalidOperationException>(() => new AttachableRecords([new FakeRecordAccess(), new FakeRecordAccess()]));
    }

    [Theory]
    [InlineData(AttachableRecordTypes.Demo, true)]
    [InlineData("unknown-records", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_record_type_is_known_only_when_registered(string? recordType, bool known)
    {
        Assert.Equal(known, new AttachableRecords([new FakeRecordAccess()]).IsKnown(recordType));
    }
}

public sealed class RequiredAttachmentsCheckTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task The_interlock_reports_a_missing_required_type()
    {
        var missing = await Check().FindMissingTypeKeysAsync(AttachableRecordTypes.Demo, TestData.RecordId, CancellationToken.None);

        Assert.Equal([AttachmentTypeKeys.DemoDocument], missing);
    }

    [Fact]
    public async Task The_interlock_is_satisfied_by_a_live_attachment()
    {
        _harness.StoredDemo("fileIdBravo00000000002");

        var missing = await Check().FindMissingTypeKeysAsync(AttachableRecordTypes.Demo, TestData.RecordId, CancellationToken.None);

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Removing_the_only_required_file_blocks_the_record_again()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");
        await _harness.RemoveAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        var missing = await Check().FindMissingTypeKeysAsync(AttachableRecordTypes.Demo, TestData.RecordId, CancellationToken.None);

        Assert.Equal([AttachmentTypeKeys.DemoDocument], missing);
    }

    [Theory]
    [InlineData(TestData.OtherRecordType, TestData.RecordId)]
    [InlineData("Not Kebab", TestData.RecordId)]
    [InlineData(AttachableRecordTypes.Demo, 0L)]
    public async Task Nothing_is_missing_for_a_record_type_without_required_types_or_a_malformed_record(string recordType, long recordId)
    {
        var missing = await Check().FindMissingTypeKeysAsync(recordType, recordId, CancellationToken.None);

        Assert.Empty(missing);
    }

    [Fact]
    public async Task The_interlock_and_the_list_agree()
    {
        var list = await _harness.ListRecordAttachments.ExecuteAsync(TestData.Demo, CancellationToken.None);
        var missing = await Check().FindMissingTypeKeysAsync(AttachableRecordTypes.Demo, TestData.RecordId, CancellationToken.None);

        Assert.Equal(list.Value.MissingRequiredTypeKeys, missing);
    }

    [Fact]
    public void The_module_code_is_the_kernel_interlock_code()
    {
        Assert.Equal(AttachmentErrorCodes.RequiredMissing, AttachmentsErrorCodes.RequiredMissing);
    }

    private RequiredAttachmentsCheck Check() => new(_harness.Store);
}

public sealed class DemoRecordAccessTests
{
    [Fact]
    public async Task Viewing_needs_the_view_permission()
    {
        Assert.True(await new DemoRecordAccess(new FakeOrganizationScope(AttachmentsPermissionKeys.View)).CanViewAsync(DemoRecord.RecordId, CancellationToken.None));
        Assert.False(await new DemoRecordAccess(new FakeOrganizationScope(AttachmentsPermissionKeys.Upload)).CanViewAsync(DemoRecord.RecordId, CancellationToken.None));
    }

    [Theory]
    [InlineData(AttachmentsPermissionKeys.Upload, true)]
    [InlineData(AttachmentsPermissionKeys.Remove, true)]
    [InlineData(AttachmentsPermissionKeys.View, false)]
    public async Task Changing_needs_upload_or_remove(string held, bool allowed)
    {
        Assert.Equal(allowed, await new DemoRecordAccess(new FakeOrganizationScope(held)).CanChangeAsync(DemoRecord.RecordId, CancellationToken.None));
    }

    [Fact]
    public async Task Any_other_record_id_does_not_exist()
    {
        var access = new DemoRecordAccess(new FakeOrganizationScope(AttachmentsPermissionKeys.View, AttachmentsPermissionKeys.Upload));

        Assert.False(await access.CanViewAsync(DemoRecord.RecordId + 1, CancellationToken.None));
        Assert.False(await access.CanChangeAsync(DemoRecord.RecordId + 1, CancellationToken.None));
        Assert.Equal(AttachableRecordTypes.Demo, access.RecordType);
    }
}

public sealed class UploadScopesTests
{
    private const long InternalRecordId = 4242;
    private static readonly AttachmentOwner Owner = new(AttachableRecordTypes.Demo, RecordId: InternalRecordId, ServiceAttachmentId: AttachmentTypeIds.DemoDocument);
    private static readonly string RecordToken = new FakePublicIdCodec().Protect(AttachableRecordIds.Domain, InternalRecordId);

    [Fact]
    public void The_scope_carries_the_record_as_its_public_id()
    {
        Assert.Equal(
            "attachments-demo|" + RecordToken + "|demo-document|11111111-2222-3333-4444-555555555555",
            TestData.Scopes.For(Owner, AttachmentTypeKeys.DemoDocument, TestData.User));
    }

    [Fact]
    public void The_scope_never_contains_the_internal_record_id()
    {
        var parts = TestData.Scopes.For(Owner, AttachmentTypeKeys.DemoDocument, TestData.User).Split(UploadScopes.Separator);

        Assert.DoesNotContain(InternalRecordId.ToString(System.Globalization.CultureInfo.InvariantCulture), parts);
    }

    [Fact]
    public void The_issued_scope_matches_its_own_record_type_and_user()
    {
        var scope = TestData.Scopes.For(Owner, AttachmentTypeKeys.DemoDocument, TestData.User);

        Assert.True(TestData.Scopes.Matches(scope, Owner, AttachmentTypeKeys.DemoDocument, TestData.User));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("attachments-demo|4242|demo-document|11111111-2222-3333-4444-555555555555")]
    [InlineData("Attachments-demo|pid~attachable-records~1092|demo-document|11111111-2222-3333-4444-555555555555")]
    [InlineData("attachments-demo|pid~attachments~1092|demo-document|11111111-2222-3333-4444-555555555555")]
    [InlineData("attachments-demo|pid~attachable-records~1093|demo-document|11111111-2222-3333-4444-555555555555")]
    [InlineData("attachments-demo|pid~attachable-records~1092|optional-photo|11111111-2222-3333-4444-555555555555")]
    [InlineData("attachments-demo|pid~attachable-records~1092|demo-document|99999999-8888-7777-6666-555555555555")]
    [InlineData("attachments-demo|pid~attachable-records~1092|demo-document|11111111-2222-3333-4444-555555555555|extra")]
    public void A_scope_of_another_record_type_or_user_or_a_decimal_id_does_not_match(string? actual)
    {
        Assert.Equal("pid~attachable-records~1092", RecordToken);
        Assert.False(TestData.Scopes.Matches(actual, Owner, AttachmentTypeKeys.DemoDocument, TestData.User));
    }

    [Fact]
    public void The_actor_stamps_with_the_clock_and_refuses_an_anonymous_request()
    {
        var clock = new FakeTimeProvider(Harness.Now);

        Assert.Equal(new AttachmentStamp(TestData.User, Harness.Now), new AttachmentActor(new FakeCurrentUser(TestData.User), clock).Stamp());
        Assert.Throws<AccessDeniedException>(() => new AttachmentActor(new FakeCurrentUser(null), clock).RequireSubject());
    }
}

public sealed class AttachmentsCatalogTests
{
    [Fact]
    public void The_catalog_declares_the_three_permissions()
    {
        Assert.Equal(
            ["attachments.remove", "attachments.upload", "attachments.view"],
            new AttachmentsPermissionCatalogSource().Permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_error_code_is_unique_and_carries_the_module_prefix()
    {
        var codes = typeof(AttachmentsErrorCodes).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(8, codes.Count);
        Assert.All(codes, code => Assert.StartsWith("ATTACHMENTS_", code, StringComparison.Ordinal));
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Wire_labels_are_stable()
    {
        Assert.Equal("attachments", AttachmentIds.Domain);
        Assert.Equal("attachable-records", AttachableRecordIds.Domain);
        Assert.Equal("attachments.delete-file", AttachmentsOutboxMessageTypes.DeleteFile);
        Assert.Equal("attachment-upload", UploadTickets.Purpose);
    }
}
