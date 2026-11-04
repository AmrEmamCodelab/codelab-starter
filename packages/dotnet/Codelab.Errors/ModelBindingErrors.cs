using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Codelab.Errors;

/// <summary>
/// The MVC model-binding and validation mapping. <c>[ApiController]</c> rejects an invalid body itself, before the
/// action runs and without throwing; <see cref="Handle"/> (the <c>InvalidModelStateResponseFactory</c>) answers it with
/// the one envelope plus a per-field <c>errors</c> dictionary: <see cref="ErrorCodes.Malformed"/> when the binder could
/// not parse the input, <see cref="ErrorCodes.Validation"/> when the values are wrong.
/// <para>
/// <b>No binder text reaches the wire.</b> The binder's defaults name CLR types (<c>System.Nullable`1[...]</c>) and
/// echo the caller's input back. <see cref="Configure"/> replaces every message the binder can produce with a
/// constant below, and <see cref="Handle"/> replaces the JSON input formatter's own text (which never consults the
/// message provider). Telling binder from validation errors is then an ALLOWLIST over our own sentences.
/// </para>
/// </summary>
public static class ModelBindingErrors
{
    /// <summary>A value arrived but could not become the target type.</summary>
    public const string UnparseableField = "The value provided is not valid for this field.";

    /// <summary>A required value never arrived.</summary>
    public const string MissingValue = "A value is required.";

    /// <summary>A numeric field got something non-numeric.</summary>
    public const string MustBeANumber = "A number is required.";

    /// <summary>The body was absent or never parsed (the same mistake from the caller's side).</summary>
    public const string MissingBody = "The request body is missing or could not be read.";

    /// <summary>The key under which a body-level failure is reported.</summary>
    public const string BodyKey = "body";

    internal const string MalformedDetail = "The request could not be parsed. See 'errors'.";
    internal const string ValidationDetail = "One or more fields are invalid. See 'errors'.";

    private const string JsonRoot = "$";
    private const string JsonPathPrefix = "$.";

    private static readonly HashSet<string> BinderVocabulary = new(StringComparer.Ordinal)
    {
        UnparseableField, MissingValue, MustBeANumber, MissingBody,
    };

    /// <summary>
    /// Replaces every message the model binder can produce. The accessors that receive the caller's value or the
    /// property name deliberately ignore their arguments: putting the value in the sentence is what must not happen.
    /// </summary>
    public static void Configure(DefaultModelBindingMessageProvider messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        messages.SetValueIsInvalidAccessor(_ => UnparseableField);
        messages.SetAttemptedValueIsInvalidAccessor((_, _) => UnparseableField);
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => UnparseableField);
        messages.SetUnknownValueIsInvalidAccessor(_ => UnparseableField);
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => UnparseableField);
        messages.SetMissingBindRequiredValueAccessor(_ => MissingValue);
        messages.SetMissingKeyOrValueAccessor(() => MissingValue);
        messages.SetValueMustNotBeNullAccessor(_ => MissingValue);
        messages.SetValueMustBeANumberAccessor(_ => MustBeANumber);
        messages.SetNonPropertyValueMustBeANumberAccessor(() => MustBeANumber);
        messages.SetMissingRequestBodyRequiredValueAccessor(() => MissingBody);
    }

    /// <summary>The <c>InvalidModelStateResponseFactory</c>: the envelope with a per-field <c>errors</c> dictionary.</summary>
    public static IActionResult Handle(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bodyParameter = context.ActionDescriptor.Parameters
            .FirstOrDefault(p => p.BindingInfo?.BindingSource == BindingSource.Body)?.Name;
        var malformed = context.ModelState.Any(entry =>
            entry.Value?.Errors.Any(error => IsBinderError(entry.Key, error, bodyParameter)) == true);

        var detail = malformed ? MalformedDetail : ValidationDetail;
        ClientErrorScrubber.MarkVetted(context.HttpContext, detail);
        var problem = new ValidationProblemDetails(Sanitize(context.ModelState, bodyParameter))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ErrorEnvelope.Title,
            Detail = detail,
            Extensions = { [ErrorEnvelope.CodeMember] = malformed ? ErrorCodes.Malformed : ErrorCodes.Validation },
        };
        return new ProblemEnvelopeResult(problem);
    }

    private static Dictionary<string, string[]> Sanitize(ModelStateDictionary modelState, string? bodyParameter)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawKey, entry) in modelState)
        {
            if (entry is null || entry.Errors.Count == 0)
            {
                continue;
            }

            var isBodyLevel = IsBodyLevel(rawKey, bodyParameter);
            var key = isBodyLevel ? BodyKey : NormaliseKey(rawKey);
            result[string.IsNullOrEmpty(key) ? BodyKey : key] = entry.Errors
                .Select(error => MessageFor(rawKey, error, bodyParameter, isBodyLevel))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return result;
    }

    private static string MessageFor(string rawKey, ModelError error, string? bodyParameter, bool isBodyLevel)
    {
        if (isBodyLevel)
        {
            return MissingBody;
        }

        // The JSON input formatter writes the deserializer's own text (type names) and never consults the provider.
        return IsBinderError(rawKey, error, bodyParameter) || string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? UnparseableField
            : error.ErrorMessage;
    }

    private static bool IsBinderError(string rawKey, ModelError error, string? bodyParameter) =>
        error.Exception is not null
        || rawKey.StartsWith(JsonRoot, StringComparison.Ordinal)
        || IsBodyLevel(rawKey, bodyParameter)
        || string.IsNullOrWhiteSpace(error.ErrorMessage)
        || BinderVocabulary.Contains(error.ErrorMessage);

    private static bool IsBodyLevel(string rawKey, string? bodyParameter) =>
        string.IsNullOrEmpty(rawKey)
        || rawKey == JsonRoot
        || (bodyParameter is not null && rawKey.Equals(bodyParameter, StringComparison.OrdinalIgnoreCase));

    private static string NormaliseKey(string key) =>
        key.StartsWith(JsonPathPrefix, StringComparison.Ordinal) ? key[JsonPathPrefix.Length..] : key;
}
