using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;

namespace Starter.Application.Tests.Workflow;

/// <summary>The one mapping of workflow refusals to the wire, and the wire values of the workflow enums.</summary>
public sealed class WorkflowRefusalsTests
{
    [Theory]
    [InlineData(WorkflowRefusal.ActionUnknown, RefusalCategory.NotFound, WorkflowErrorCodes.ActionUnknown)]
    [InlineData(WorkflowRefusal.NotAllowedFromStatus, RefusalCategory.Conflict, WorkflowErrorCodes.ActionNotAllowed)]
    [InlineData(WorkflowRefusal.Forbidden, RefusalCategory.Forbidden, WorkflowErrorCodes.Forbidden)]
    public void A_refusal_without_a_field_maps_to_its_status_and_code(WorkflowRefusal refusal, RefusalCategory category, string code)
    {
        var mapped = WorkflowRefusals.ToRefusal(new TransitionDecision.Refused(refusal, ReasonFieldKey: null));

        Assert.Equal(category, mapped.Category);
        Assert.Equal(code, mapped.Code);
        Assert.Empty(mapped.FieldErrors);
    }

    [Theory]
    [InlineData(WorkflowRefusal.ReasonRequired, WorkflowErrorCodes.ReasonRequired, ValidationMessages.Required)]
    [InlineData(WorkflowRefusal.ReasonTooLong, WorkflowErrorCodes.ReasonTooLong, ValidationMessages.TooLong)]
    public void A_reason_refusal_is_a_400_on_the_rule_reason_field(WorkflowRefusal refusal, string code, string message)
    {
        var mapped = WorkflowRefusals.ToRefusal(new TransitionDecision.Refused(refusal, ReasonFieldKey: "returnNote"));

        Assert.Equal(RefusalCategory.Invalid, mapped.Category);
        Assert.Equal(code, mapped.Code);
        Assert.Equal([message], mapped.FieldErrors["returnNote"]);
    }

    [Fact]
    public void A_reason_refusal_without_a_field_uses_the_default_reason_field()
    {
        var mapped = WorkflowRefusals.ToRefusal(new TransitionDecision.Refused(WorkflowRefusal.ReasonTooLong, ReasonFieldKey: null));

        Assert.Equal([WorkflowRefusals.DefaultReasonField], mapped.FieldErrors.Keys);
    }

    [Fact]
    public void An_undefined_refusal_is_a_programming_fault()
    {
        const WorkflowRefusal undefined = (WorkflowRefusal)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => WorkflowRefusals.ToRefusal(new TransitionDecision.Refused(undefined, ReasonFieldKey: null)));
    }

    [Fact]
    public void The_error_codes_are_the_wire_contract()
    {
        Assert.Equal("WORKFLOW_ACTION_NOT_ALLOWED", WorkflowErrorCodes.ActionNotAllowed);
        Assert.Equal("WORKFLOW_FORBIDDEN", WorkflowErrorCodes.Forbidden);
        Assert.Equal("WORKFLOW_REASON_REQUIRED", WorkflowErrorCodes.ReasonRequired);
        Assert.Equal("WORKFLOW_REASON_TOO_LONG", WorkflowErrorCodes.ReasonTooLong);
        Assert.Equal("WORKFLOW_ACTION_UNKNOWN", WorkflowErrorCodes.ActionUnknown);
    }

    [Fact]
    public void The_enum_values_are_explicit_and_frozen()
    {
        Assert.Equal([1, 2, 3], Enum.GetValues<WorkflowAudience>().Select(value => (int)value));
        Assert.Equal([1, 2, 3], Enum.GetValues<ReasonRequirement>().Select(value => (int)value));
        Assert.Equal([1, 2, 3], Enum.GetValues<TransitionSide>().Select(value => (int)value));
        Assert.Equal([1, 2, 3, 4, 5], Enum.GetValues<WorkflowRefusal>().Select(value => (int)value));
        Assert.Equal(Enumerable.Range(1, 11), Enum.GetValues<CatalogViolationKind>().Select(value => (int)value));
        Assert.Equal(1000, WorkflowLimits.MaxReasonLength);
    }

    [Theory]
    [InlineData(WorkflowAudience.Requester, "r")]
    [InlineData(WorkflowAudience.Staff, "s")]
    [InlineData(WorkflowAudience.Admin, "a")]
    public void Status_wording_picks_the_key_of_the_audience(WorkflowAudience audience, string expected) =>
        Assert.Equal(expected, new StatusWording(RequesterKey: "r", StaffKey: "s", AdminKey: "a").For(audience));

    [Fact]
    public void Status_wording_refuses_an_undefined_audience() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatusWording("r", "s", "a").For((WorkflowAudience)9));
}
