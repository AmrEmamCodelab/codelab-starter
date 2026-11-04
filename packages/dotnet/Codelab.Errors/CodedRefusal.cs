using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Codelab.Errors;

/// <summary>
/// An expected business "no" returned as a VALUE, not thrown: a use case returns
/// <c>CodedRefusal.Conflict(ServiceRequestErrorCodes.NotADraft)</c>, and the endpoint maps it once with
/// <see cref="ToResult"/> (minimal API) or <see cref="ToActionResult"/> (MVC) into the one envelope with that code.
/// The code is the whole contract: the client renders <c>errors.&lt;CODE&gt;</c>, so a refusal carries no sentence.
/// </summary>
public sealed record CodedRefusal
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private CodedRefusal(RefusalKind kind, string code, IReadOnlyDictionary<string, string[]> errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Kind = kind;
        Code = code;
        Errors = errors;
    }

    /// <summary>The kind, which decides the status.</summary>
    public RefusalKind Kind { get; }

    /// <summary>The machine code (<see cref="ErrorCodes"/> or a module's own).</summary>
    public string Code { get; }

    /// <summary>Per-field messages of an <see cref="RefusalKind.Invalid"/> refusal; empty otherwise.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>The HTTP status of <see cref="Kind"/>.</summary>
    public int Status => Kind switch
    {
        RefusalKind.Invalid => StatusCodes.Status400BadRequest,
        RefusalKind.Forbidden => StatusCodes.Status403Forbidden,
        RefusalKind.NotFound => StatusCodes.Status404NotFound,
        RefusalKind.Conflict => StatusCodes.Status409Conflict,
        RefusalKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        _ => throw new InvalidOperationException($"Unknown refusal kind {(int)Kind}."),
    };

    /// <summary>A validator's refusal: every field error at once, under <paramref name="code"/>.</summary>
    public static CodedRefusal Invalid(IReadOnlyDictionary<string, string[]> errors, string code = ErrorCodes.Validation)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new CodedRefusal(RefusalKind.Invalid, code, errors);
    }

    /// <summary>403 with <paramref name="code"/>.</summary>
    public static CodedRefusal Forbidden(string code = ErrorCodes.Forbidden) => new(RefusalKind.Forbidden, code, NoErrors);

    /// <summary>404 with <paramref name="code"/>.</summary>
    public static CodedRefusal NotFound(string code = ErrorCodes.NotFound) => new(RefusalKind.NotFound, code, NoErrors);

    /// <summary>409 with <paramref name="code"/>.</summary>
    public static CodedRefusal Conflict(string code = ErrorCodes.Conflict) => new(RefusalKind.Conflict, code, NoErrors);

    /// <summary>422 with <paramref name="code"/>.</summary>
    public static CodedRefusal Unprocessable(string code = ErrorCodes.BusinessRule) => new(RefusalKind.Unprocessable, code, NoErrors);

    /// <summary>The envelope for a minimal-API endpoint.</summary>
    public IResult ToResult() => new ProblemEnvelopeResult(ToProblemDetails());

    /// <summary>The envelope for an MVC action.</summary>
    public IActionResult ToActionResult() => new ProblemEnvelopeResult(ToProblemDetails());

    /// <summary>The problem details of this refusal: the status's table sentence, the code, and the errors when any.</summary>
    public ProblemDetails ToProblemDetails()
    {
        var problem = Errors.Count > 0
            ? new HttpValidationProblemDetails(Errors.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal))
            : new ProblemDetails();
        problem.Status = Status;
        problem.Title = ErrorEnvelope.Title;
        problem.Detail = FrameworkStatusCodes.Describe(Status).Detail;
        problem.Extensions[ErrorEnvelope.CodeMember] = Code;
        return problem;
    }
}
