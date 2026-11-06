using Codelab.Security.Cors;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Api.Hosting;

/// <summary>
/// What this host needs from Codelab.Security beyond the package's own checks, refused at start: upload tickets on
/// (the browser's only credential), signed receipts on (the API links nothing without one), and no global CORS policy
/// (only the upload endpoint is reachable cross-origin, through <c>Attachments:Cors</c>).
/// </summary>
internal sealed class RequiredSecurityFeaturesValidator :
    IValidateOptions<SealedTicketOptions>,
    IValidateOptions<UploadSafetyOptions>,
    IValidateOptions<CodelabCorsOptions>
{
    public ValidateOptionsResult Validate(string? name, SealedTicketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Enabled
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{SealedTicketOptions.SectionName}:{nameof(SealedTicketOptions.Enabled)} must be true: the upload ticket is the browser's only credential here.");
    }

    public ValidateOptionsResult Validate(string? name, UploadSafetyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ReceiptsEnabled
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{UploadSafetyOptions.SectionName}:{nameof(UploadSafetyOptions.ReceiptsEnabled)} must be true: the API links a file only with its signed receipt.");
    }

    public ValidateOptionsResult Validate(string? name, CodelabCorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AllowedOrigins.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{CodelabCorsOptions.SectionName}:{nameof(CodelabCorsOptions.AllowedOrigins)} must stay empty here; set {UploadCorsOptions.SectionName}:{nameof(UploadCorsOptions.AllowedOrigins)} (the upload endpoint only).");
    }
}
