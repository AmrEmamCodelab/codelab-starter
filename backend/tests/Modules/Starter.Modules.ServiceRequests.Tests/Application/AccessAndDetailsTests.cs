using Starter.Application.Records;
using Starter.Domain.Lookups;
using Starter.Domain.Records;
using Starter.Modules.ServiceRequests.Application.Drafts;
using Starter.Modules.ServiceRequests.Application.Validation;
using Starter.Modules.ServiceRequests.Domain;
using Starter.Modules.ServiceRequests.Tests.Fakes;

namespace Starter.Modules.ServiceRequests.Tests.Application;

public sealed class AccessAndDetailsTests
{
    private static Harness Submitted(Guid subject) =>
        new Harness(subject).WithRecord(TestValues.Record(ServiceRequestStatuses.Submitted));

    private static GetServiceRequestDetails Details(Harness harness) => new(harness.Access, harness.Store, harness.Categories, harness.Views);

    [Fact]
    public async Task A_caller_who_may_see_the_record_gets_the_details_with_the_category_names()
    {
        var harness = Submitted(TestValues.OwnerSubject);
        harness.Visibility.Visible.Add(TestValues.RecordId);
        var row = ServiceRequest.StartFor(TestValues.RecordId);
        row.ApplyRequestStep(new RequestStepFields(ServiceRequestCategoryIds.Complaint, "Noise complaint"));
        harness.Store.Add(row);

        var outcome = await Details(harness).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal("SR-2026-001", outcome.Value.Number);
        Assert.Equal(FakeCodec.CategoryToken(ServiceRequestCategoryIds.Complaint), outcome.Value.Draft.CategoryToken);
        Assert.Equal(($"اسم {ServiceRequestCategoryIds.Complaint}", $"Name {ServiceRequestCategoryIds.Complaint}"), (outcome.Value.CategoryNameAr, outcome.Value.CategoryNameEn));
    }

    [Fact]
    public async Task A_retired_category_keeps_its_id_but_has_no_names()
    {
        var harness = Submitted(TestValues.OwnerSubject);
        harness.Visibility.Visible.Add(TestValues.RecordId);
        var row = ServiceRequest.StartFor(TestValues.RecordId);
        row.ApplyRequestStep(new RequestStepFields(ServiceRequestCategoryIds.General, "General question"));
        harness.Store.Add(row);
        harness.Lookups.Retired.Add(ServiceRequestCategoryIds.General);

        var outcome = await Details(harness).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.NotNull(outcome.Value.Draft.CategoryToken);
        Assert.Null(outcome.Value.CategoryNameEn);
    }

    [Fact]
    public async Task A_record_the_records_module_hides_has_no_details()
    {
        var harness = Submitted(TestValues.OwnerSubject);

        var outcome = await Details(harness).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal(RecordsErrorCodes.NotFound, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task A_never_submitted_draft_hidden_from_a_queue_holder_answers_404_with_no_field_leaked()
    {
        var harness = new Harness(TestValues.OtherSubject).WithRecord(TestValues.Record(ServiceRequestStatuses.Draft));
        var row = ServiceRequest.StartFor(TestValues.RecordId);
        row.ApplyDetailsStep(new DetailsStepFields("A private draft description, never submitted.", TestValues.ValidPhone, PreferredDate: null));
        harness.Store.Add(row);

        var outcome = await Details(harness).ExecuteAsync(TestValues.RecordId, CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RecordsErrorCodes.NotFound, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task The_visibility_decision_is_asked_for_the_service_request_record()
    {
        var harness = Submitted(TestValues.OtherSubject);
        harness.Visibility.Visible.Add(TestValues.RecordId);

        Assert.NotNull(await harness.Access.FindViewableAsync(TestValues.RecordId, CancellationToken.None));
        Assert.Equal([new RecordRef(TestValues.RecordId, ServiceRequestRecord.RecordType)], harness.Visibility.Asked);
    }

    [Fact]
    public async Task A_record_of_another_type_is_never_viewable_and_the_decision_is_not_asked()
    {
        var harness = new Harness().WithRecord(TestValues.Record(ServiceRequestStatuses.Submitted, recordType: "other-record"));
        harness.Visibility.Visible.Add(TestValues.RecordId);

        Assert.Null(await harness.Access.FindViewableAsync(TestValues.RecordId, CancellationToken.None));
        Assert.Empty(harness.Visibility.Asked);
    }

    [Fact]
    public async Task A_missing_record_is_not_viewable()
    {
        var harness = new Harness();
        harness.Visibility.Visible.Add(TestValues.RecordId);

        Assert.Null(await harness.Access.FindViewableAsync(TestValues.RecordId, CancellationToken.None));
        Assert.Null(await harness.Access.FindViewableAsync(0, CancellationToken.None));
    }

    [Fact]
    public async Task The_caller_is_resolved_once_per_scope()
    {
        var harness = Submitted(TestValues.OwnerSubject);

        await harness.Access.FindOwnedAsync(TestValues.RecordId, CancellationToken.None);
        await harness.Access.FindOwnedAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal(1, harness.Users.Lookups);
    }

    [Fact]
    public async Task The_submit_validator_accepts_a_complete_request()
    {
        var harness = new Harness();
        harness.Store.Add(CompleteRow());

        Assert.Empty(await harness.SubmitValidator().ValidateForSubmitAsync(TestValues.RecordId, CancellationToken.None));
        Assert.Equal(ServiceRequestRecord.RecordType, harness.SubmitValidator().RecordType);
    }

    [Fact]
    public async Task The_submit_validator_refuses_a_request_that_skipped_step_two()
    {
        var harness = new Harness();
        var row = ServiceRequest.StartFor(TestValues.RecordId);
        row.ApplyRequestStep(new RequestStepFields(ServiceRequestCategoryIds.General, "Passport renewal"));
        harness.Store.Add(row);

        var errors = await harness.SubmitValidator().ValidateForSubmitAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal(
            [new FieldError(ServiceRequestFields.ContactPhone, ServiceRequestValidationCodes.ContactPhoneRequired), new FieldError(ServiceRequestFields.Description, ServiceRequestValidationCodes.DescriptionRequired)],
            errors);
    }

    [Fact]
    public async Task The_submit_validator_refuses_every_required_field_of_a_record_without_a_row()
    {
        var errors = await new Harness().SubmitValidator().ValidateForSubmitAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal(
            [ServiceRequestFields.CategoryId, ServiceRequestFields.ContactPhone, ServiceRequestFields.Description, ServiceRequestFields.Title],
            errors.Select(error => error.Field));
    }

    [Fact]
    public async Task The_submit_validator_refuses_a_category_retired_since_the_save()
    {
        var harness = new Harness();
        harness.Store.Add(CompleteRow());
        harness.Lookups.Retired.Add(ServiceRequestCategoryIds.DataUpdate);

        var errors = await harness.SubmitValidator().ValidateForSubmitAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal([new FieldError(ServiceRequestFields.CategoryId, ServiceRequestValidationCodes.CategoryUnknown)], errors);
    }

    [Fact]
    public async Task The_submit_validator_refuses_a_preferred_date_that_has_passed_since_the_save()
    {
        var harness = new Harness();
        var row = CompleteRow();
        row.ApplyDetailsStep(new DetailsStepFields("I need to renew my passport before travelling.", TestValues.ValidPhone, TestValues.Today.AddDays(-1)));
        harness.Store.Add(row);

        var errors = await harness.SubmitValidator().ValidateForSubmitAsync(TestValues.RecordId, CancellationToken.None);

        Assert.Equal([new FieldError(ServiceRequestFields.PreferredDate, ServiceRequestValidationCodes.PreferredDateInPast)], errors);
    }

    private static ServiceRequest CompleteRow()
    {
        var row = ServiceRequest.StartFor(TestValues.RecordId);
        row.ApplyRequestStep(new RequestStepFields(ServiceRequestCategoryIds.DataUpdate, "Update my address"));
        row.ApplyDetailsStep(new DetailsStepFields("Please update the address on my file to the new one.", TestValues.OtherPhone, PreferredDate: null));
        return row;
    }
}
