using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Codelab.Errors.Tests;

/// <summary>An expected business "no" is a value with a code and a status, mapped once into the envelope.</summary>
public sealed class CodedRefusalTests
{
    [Fact]
    public void Each_kind_maps_to_its_status_and_default_code()
    {
        Assert.Equal((StatusCodes.Status403Forbidden, ErrorCodes.Forbidden), Of(CodedRefusal.Forbidden()));
        Assert.Equal((StatusCodes.Status404NotFound, ErrorCodes.NotFound), Of(CodedRefusal.NotFound()));
        Assert.Equal((StatusCodes.Status409Conflict, ErrorCodes.Conflict), Of(CodedRefusal.Conflict()));
        Assert.Equal((StatusCodes.Status422UnprocessableEntity, ErrorCodes.BusinessRule), Of(CodedRefusal.Unprocessable()));
        Assert.Equal((StatusCodes.Status400BadRequest, ErrorCodes.Validation), Of(CodedRefusal.Invalid(new Dictionary<string, string[]>())));
    }

    [Fact]
    public void A_specific_code_is_carried_on_the_problem_details()
    {
        var problem = CodedRefusal.Conflict("RECORD_NOT_A_DRAFT").ToProblemDetails();

        Assert.Equal("RECORD_NOT_A_DRAFT", problem.Extensions[ErrorEnvelope.CodeMember]);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal(ErrorEnvelope.Title, problem.Title);
    }

    [Fact]
    public void An_invalid_refusal_carries_every_field_error()
    {
        var errors = new Dictionary<string, string[]> { ["name"] = ["A value is required."], ["count"] = ["A number is required."] };

        var problem = Assert.IsType<HttpValidationProblemDetails>(CodedRefusal.Invalid(errors).ToProblemDetails());

        Assert.Equal(2, problem.Errors.Count);
    }

    [Fact]
    public void A_blank_code_is_refused()
    {
        Assert.Throws<ArgumentException>(() => CodedRefusal.NotFound(" "));
    }

    [Fact]
    public void Kinds_have_explicit_stable_values()
    {
        Assert.Equal([1, 2, 3, 4, 5], Enum.GetValues<RefusalKind>().Select(k => (int)k));
    }

    [Fact]
    public void The_results_report_status_and_content_type()
    {
        var result = Assert.IsType<ProblemEnvelopeResult>(CodedRefusal.NotFound().ToResult());

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(ErrorEnvelope.ProblemJsonMediaType, result.ContentType);
        Assert.IsType<ProblemEnvelopeResult>(CodedRefusal.NotFound().ToActionResult());
        Assert.Throws<ArgumentException>(() => new ProblemEnvelopeResult(new ProblemDetails()));
    }

    private static (int Status, string Code) Of(CodedRefusal refusal) => (refusal.Status, refusal.Code);
}
