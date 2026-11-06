using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Validation;

namespace Starter.Application.Tests.Records;

/// <summary>SubmitResult and TransitionResult: construction rules and the outcome they become.</summary>
public sealed class RecordResultsTests
{
    private const string Number = "TR-2026-001";
    private const string TitleField = "title";
    private const int NewStatus = 3;
    private const string ConflictCode = "TEST_RECORD_CONFLICT";

    [Fact]
    public void A_submitted_result_becomes_a_successful_outcome_with_the_number()
    {
        var outcome = new SubmitResult.Submitted(Number).ToOutcome();

        Assert.True(outcome.IsSuccess);
        Assert.Equal(Number, outcome.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_submitted_result_without_a_number_is_refused(string? number) =>
        Assert.Throws<ArgumentException>(() => new SubmitResult.Submitted(number!));

    [Fact]
    public void A_refused_submit_becomes_the_centrally_mapped_refusal()
    {
        var outcome = SubmitResult.Refuse(SubmitRefusal.AttachmentsMissing).ToOutcome();

        Assert.False(outcome.IsSuccess);
        Assert.Equal(SubmitRefusals.ToRefusal(SubmitRefusal.AttachmentsMissing, fieldErrors: null), outcome.Refusal);
    }

    [Fact]
    public void An_invalid_submit_carries_its_field_errors_to_the_outcome()
    {
        var result = SubmitResult.Invalid([new FieldError(TitleField, ValidationMessages.Required)]);

        var refusal = result.ToOutcome().Refusal!;

        Assert.Equal(PlatformErrorCodes.Validation, refusal.Code);
        Assert.Equal([ValidationMessages.Required], refusal.FieldErrors[TitleField]);
    }

    [Fact]
    public void An_inconsistent_refused_submit_fails_where_it_is_built()
    {
        Assert.Throws<ArgumentException>(() => SubmitResult.Refuse(SubmitRefusal.ValidationFailed));
        Assert.Throws<ArgumentException>(() => SubmitResult.Invalid([]));
        Assert.Throws<ArgumentException>(() =>
            new SubmitResult.Refused(SubmitRefusal.NotFound, [new FieldError(TitleField, ValidationMessages.Required)]));
    }

    [Fact]
    public void A_transitioned_result_becomes_a_successful_outcome_with_the_new_status()
    {
        var outcome = new TransitionResult.Transitioned(NewStatus).ToOutcome();

        Assert.Equal(NewStatus, outcome.Value);
    }

    [Fact]
    public void A_refused_transition_keeps_its_refusal()
    {
        var refusal = Refusal.Conflict(ConflictCode);

        var outcome = TransitionResult.Refuse(refusal).ToOutcome();

        Assert.Same(refusal, outcome.Refusal);
    }

    [Fact]
    public void A_refused_transition_without_a_refusal_is_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(() => TransitionResult.Refuse(null!));

    [Theory]
    [InlineData(null, ValidationMessages.Required)]
    [InlineData(TitleField, null)]
    [InlineData(" ", ValidationMessages.Required)]
    [InlineData(TitleField, "")]
    public void A_field_error_names_its_field_and_code(string? field, string? code) =>
        Assert.Throws<ArgumentException>(() => new FieldError(field!, code!));
}
