using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Domain.Lookups;
using Starter.Modules.ServiceRequests.Application;
using Starter.Modules.ServiceRequests.Application.Drafts;
using Starter.Modules.ServiceRequests.Application.Validation;
using Starter.Modules.ServiceRequests.Domain;
using Starter.Modules.ServiceRequests.Tests.Fakes;

namespace Starter.Modules.ServiceRequests.Tests.Application;

public sealed class DraftUseCaseTests
{
    private const string Title = "Passport renewal";
    private const string Description = "I need to renew my passport before travelling.";
    private const string Phone = TestValues.ValidPhone;

    private static readonly string Category = FakeCodec.CategoryToken(ServiceRequestCategoryIds.Certificate);

    private static SaveServiceRequestDraftCommand StepOne(string? category = null, string? title = Title) =>
        new(CategoryId: category ?? Category, Title: title, Description: null, ContactPhone: null, PreferredDate: null, Step: (int)ServiceRequestDraftStep.Request);

    private static SaveServiceRequestDraftCommand StepTwo(string? date = "2026-11-20") =>
        new(CategoryId: null, Title: null, Description: Description, ContactPhone: Phone, PreferredDate: date, Step: (int)ServiceRequestDraftStep.Details);

    private static Harness OwnedDraft(int status = ServiceRequestStatuses.Draft) =>
        new Harness().WithRecord(TestValues.Record(status));

    [Fact]
    public async Task Create_asks_the_kernel_for_a_draft_of_the_sample_type_and_adds_its_empty_row()
    {
        var harness = new Harness();

        var outcome = await new CreateServiceRequestDraft(harness.Lifecycle, harness.Store).ExecuteAsync(CancellationToken.None);

        Assert.Equal(TestValues.RecordId, outcome.Value);
        Assert.Equal([ServiceRequestRecord.RecordType], harness.Lifecycle.CreatedTypes);
        var row = harness.Store.Rows[TestValues.RecordId];
        Assert.Null(row.Title);
        Assert.Equal(1, harness.Store.Saves);
    }

    [Fact]
    public async Task Saving_step_one_writes_only_the_category_and_title()
    {
        var harness = OwnedDraft();

        var outcome = await harness.SaveDraft().ExecuteAsync(
            TestValues.RecordId, StepOne() with { Description = "ignored because it is not step 2" }, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var row = harness.Store.Rows[TestValues.RecordId];
        Assert.Equal((ServiceRequestCategoryIds.Certificate, Title), (row.CategoryId, row.Title));
        Assert.Null(row.Description);
    }

    [Fact]
    public async Task Saving_step_two_writes_the_details_and_keeps_step_one()
    {
        var harness = OwnedDraft();
        await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepTwo() with { Title = "x" }, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var row = harness.Store.Rows[TestValues.RecordId];
        Assert.Equal((Title, Description, Phone, new DateOnly(2026, 11, 20)), (row.Title, row.Description, row.ContactPhone, row.PreferredDate));
    }

    [Fact]
    public async Task Saving_step_three_validates_and_writes_nothing()
    {
        var harness = OwnedDraft();
        var command = new SaveServiceRequestDraftCommand(null, null, null, null, null, (int)ServiceRequestDraftStep.Attachments);

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, command, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Null(harness.Store.Rows[TestValues.RecordId].CategoryId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(4)]
    public async Task A_save_without_a_known_step_is_invalid(int? step)
    {
        var harness = OwnedDraft();

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne() with { Step = step }, CancellationToken.None);

        AssertFieldError(outcome.Refusal, SaveServiceRequestDraftValidator.StepField, ServiceRequestValidationCodes.StepInvalid);
        Assert.Empty(harness.Store.Rows);
    }

    [Fact]
    public async Task An_invalid_step_is_refused_with_its_field_errors_before_anything_is_read()
    {
        var harness = OwnedDraft();

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(title: "Abc"), CancellationToken.None);

        AssertFieldError(outcome.Refusal, ServiceRequestFields.Title, ServiceRequestValidationCodes.TitleTooShort);
        Assert.Equal(0, harness.Users.Lookups);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("tok:lookup-items:99")]
    [InlineData("tok:attachable-records:1")]
    public async Task A_category_that_is_not_an_active_value_is_a_field_error(string token)
    {
        var harness = OwnedDraft();

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(category: token), CancellationToken.None);

        AssertFieldError(outcome.Refusal, ServiceRequestFields.CategoryId, ServiceRequestValidationCodes.CategoryUnknown);
        Assert.Equal(0, harness.Store.Saves);
    }

    [Fact]
    public async Task A_retired_category_is_refused_for_new_input()
    {
        var harness = OwnedDraft();
        harness.Lookups.Retired.Add(ServiceRequestCategoryIds.Certificate);

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);

        AssertFieldError(outcome.Refusal, ServiceRequestFields.CategoryId, ServiceRequestValidationCodes.CategoryUnknown);
    }

    [Fact]
    public async Task Someone_else_s_draft_is_not_found()
    {
        var harness = new Harness().WithRecord(TestValues.Record(ServiceRequestStatuses.Draft, ownerUserId: TestValues.OtherUserId));

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);

        Assert.Equal((RefusalCategory.NotFound, RecordsErrorCodes.NotFound), (outcome.Refusal!.Category, outcome.Refusal.Code));
    }

    [Fact]
    public async Task A_record_of_another_record_type_is_not_found()
    {
        var harness = new Harness().WithRecord(TestValues.Record(ServiceRequestStatuses.Draft, recordType: "test-record"));

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);

        Assert.Equal(RecordsErrorCodes.NotFound, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task An_anonymous_caller_owns_nothing()
    {
        var harness = OwnedDraft();
        harness.CurrentUser.SubjectId = null;

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);

        Assert.Equal(RecordsErrorCodes.NotFound, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task A_returned_request_is_still_editable()
    {
        var harness = OwnedDraft(ServiceRequestStatuses.Returned);

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
    }

    [Theory]
    [InlineData(ServiceRequestStatuses.Submitted)]
    [InlineData(ServiceRequestStatuses.UnderReview)]
    [InlineData(ServiceRequestStatuses.Resubmitted)]
    [InlineData(ServiceRequestStatuses.Approved)]
    [InlineData(ServiceRequestStatuses.Cancelled)]
    public async Task A_request_past_draft_and_returned_is_not_editable(int status)
    {
        var harness = OwnedDraft(status);

        var outcome = await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, ServiceRequestsErrorCodes.NotEditable), (outcome.Refusal!.Category, outcome.Refusal.Code));
    }

    [Fact]
    public async Task A_save_adds_the_row_when_the_draft_has_none_yet()
    {
        var harness = OwnedDraft();

        await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);

        Assert.Equal(Title, harness.Store.Rows[TestValues.RecordId].Title);
    }

    [Fact]
    public async Task The_draft_resumes_with_the_saved_fields_as_wire_values()
    {
        var harness = OwnedDraft();
        await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepOne(), CancellationToken.None);
        await harness.SaveDraft().ExecuteAsync(TestValues.RecordId, StepTwo(), CancellationToken.None);

        var outcome = await new GetServiceRequestDraft(harness.Access, harness.Store, harness.Views).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal(new ServiceRequestDraftView(TestValues.RecordId, Category, Title, Description, Phone, "2026-11-20"), outcome.Value);
    }

    [Fact]
    public async Task A_draft_without_a_row_resumes_empty()
    {
        var harness = OwnedDraft();

        var outcome = await new GetServiceRequestDraft(harness.Access, harness.Store, harness.Views).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal(new ServiceRequestDraftView(TestValues.RecordId, null, null, null, null, null), outcome.Value);
    }

    [Fact]
    public async Task Someone_else_s_draft_cannot_be_resumed()
    {
        var harness = OwnedDraft();
        harness.CurrentUser.SubjectId = TestValues.OtherSubject;

        var outcome = await new GetServiceRequestDraft(harness.Access, harness.Store, harness.Views).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal(RecordsErrorCodes.NotFound, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task Submit_hands_the_record_to_the_kernel_and_returns_the_number()
    {
        var harness = new Harness();

        var outcome = await new SubmitServiceRequest(harness.Lifecycle).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal("SR-2026-001", outcome.Value);
        var submitted = Assert.Single(harness.Lifecycle.Submitted);
        Assert.Equal((TestValues.RecordId, ServiceRequestRecord.RecordType), (submitted.Id, submitted.RecordType));
    }

    [Fact]
    public async Task A_submit_refusal_is_mapped_by_the_kernel_mapping()
    {
        var harness = new Harness();
        harness.Lifecycle.SubmitResult = SubmitResult.Invalid([new FieldError(ServiceRequestFields.Description, ServiceRequestValidationCodes.DescriptionRequired)]);

        var outcome = await new SubmitServiceRequest(harness.Lifecycle).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        AssertFieldError(outcome.Refusal, ServiceRequestFields.Description, ServiceRequestValidationCodes.DescriptionRequired);
    }

    [Fact]
    public async Task A_missing_attachment_refusal_is_the_interlock_conflict()
    {
        var harness = new Harness();
        harness.Lifecycle.SubmitResult = SubmitResult.Refuse(SubmitRefusal.AttachmentsMissing);

        var outcome = await new SubmitServiceRequest(harness.Lifecycle).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, Starter.Application.Attachments.AttachmentErrorCodes.RequiredMissing), (outcome.Refusal!.Category, outcome.Refusal.Code));
    }

    [Fact]
    public async Task A_submit_of_an_impossible_id_never_reaches_the_kernel()
    {
        var harness = new Harness();

        var outcome = await new SubmitServiceRequest(harness.Lifecycle).ExecuteAsync(0, CancellationToken.None);

        Assert.Equal(RecordsErrorCodes.NotFound, outcome.Refusal!.Code);
        Assert.Empty(harness.Lifecycle.Submitted);
    }

    private static void AssertFieldError(Refusal? refusal, string field, string code)
    {
        Assert.NotNull(refusal);
        Assert.Equal(RefusalCategory.Invalid, refusal.Category);
        Assert.Equal([code], refusal.FieldErrors[field]);
    }
}
