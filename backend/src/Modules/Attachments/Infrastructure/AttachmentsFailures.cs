using Codelab.Errors;
using Codelab.Errors.Classifiers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Starter.Modules.Attachments.Application;

namespace Starter.Modules.Attachments.Infrastructure;

/// <summary>
/// The Attachments module's entry in the error classifier table: <see cref="AttachmentStorageUnavailableException"/>
/// answers 503 <see cref="AttachmentsErrorCodes.StorageUnavailable"/>, ahead of the generic 502 for every
/// <see cref="UpstreamUnavailableException"/> (the exception derives from it, so order decides).
/// </summary>
public static class AttachmentsFailures
{
    /// <summary>The failure an attachment service outage is reported as.</summary>
    public static Failure StorageUnavailable { get; } = new(
        Status: StatusCodes.Status503ServiceUnavailable,
        Code: AttachmentsErrorCodes.StorageUnavailable,
        Detail: FrameworkStatusCodes.Describe(StatusCodes.Status503ServiceUnavailable).Detail,
        Level: LogLevel.Warning);

    /// <summary>
    /// Inserts the classifier right before the first <see cref="UpstreamUnavailableClassifier"/> (or first, when the
    /// table has none), once: a second call changes nothing.
    /// </summary>
    public static CodelabErrorsOptions AddAttachmentsFailures(this CodelabErrorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Classifiers.Any(classifier => classifier is ExceptionTypeClassifier<AttachmentStorageUnavailableException>))
        {
            return options;
        }

        var upstream = options.Classifiers.TakeWhile(classifier => classifier is not UpstreamUnavailableClassifier).Count();
        var position = upstream < options.Classifiers.Count ? upstream : 0;
        options.Classifiers.Insert(position, new ExceptionTypeClassifier<AttachmentStorageUnavailableException>(_ => StorageUnavailable));
        return options;
    }
}
