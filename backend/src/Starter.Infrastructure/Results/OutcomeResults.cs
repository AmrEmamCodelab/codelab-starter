using Codelab.Errors;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;

namespace Starter.Infrastructure.Results;

/// <summary>
/// Maps a use case's <see cref="Outcome{T}"/> to an MVC result, once, in the endpoint: a success becomes 200 with the
/// mapped body, a <see cref="Refusal"/> becomes the Codelab.Errors problem+json envelope with its code.
/// </summary>
public static class OutcomeResults
{
    /// <summary>200 with <paramref name="map"/> of the value, or the refusal's problem+json.</summary>
    public static ActionResult<TResponse> ToActionResult<T, TResponse>(this Outcome<T> outcome, Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(map);
        return outcome.IsSuccess ? new OkObjectResult(map(outcome.Value)) : outcome.Refusal.ToActionResult();
    }

    /// <summary>204, or the refusal's problem+json.</summary>
    public static ActionResult ToNoContentResult<T>(this Outcome<T> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.IsSuccess ? new NoContentResult() : outcome.Refusal.ToActionResult();
    }

    /// <summary>The problem+json result of a refusal.</summary>
    public static ActionResult ToActionResult(this Refusal refusal) => new RefusalResult(refusal.ToCodedRefusal());

    /// <summary>The Codelab.Errors refusal of a kernel refusal (same code, same field errors, the category's status).</summary>
    public static CodedRefusal ToCodedRefusal(this Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return refusal.Category switch
        {
            RefusalCategory.Invalid => CodedRefusal.Invalid(refusal.FieldErrors, refusal.Code),
            RefusalCategory.Forbidden => CodedRefusal.Forbidden(refusal.Code),
            RefusalCategory.NotFound => CodedRefusal.NotFound(refusal.Code),
            RefusalCategory.Conflict => CodedRefusal.Conflict(refusal.Code),
            RefusalCategory.Unprocessable => CodedRefusal.Unprocessable(refusal.Code),
            _ => throw new InvalidOperationException($"Unknown refusal category {(int)refusal.Category}."),
        };
    }

    /// <summary>Wraps the envelope result so it can stand in an <see cref="ActionResult{TValue}"/>.</summary>
    private sealed class RefusalResult(CodedRefusal refusal) : ActionResult
    {
        public override Task ExecuteResultAsync(ActionContext context) => refusal.ToActionResult().ExecuteResultAsync(context);
    }
}
