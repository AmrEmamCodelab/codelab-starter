using System.Text.Json.Serialization;

namespace Starter.DevIdp.Protocol;

/// <summary>An OAuth 2.0 error: a code from <see cref="OidcErrorCodes"/> and a human-readable description.</summary>
/// <param name="Code">The <c>error</c> value.</param>
/// <param name="Description">The <c>error_description</c> value.</param>
internal sealed record OidcError(
    [property: JsonPropertyName(OidcParameterNames.Error)] string Code,
    [property: JsonPropertyName(OidcParameterNames.ErrorDescription)] string Description);
