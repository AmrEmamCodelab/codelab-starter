namespace Starter.Application.Attachments;

/// <summary>
/// The attachment error codes another module returns (the Attachments module owns the rest). The client renders
/// <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// </summary>
public static class AttachmentErrorCodes
{
    /// <summary>
    /// The record still misses an attachment of a required type (409); returned by the module whose action the
    /// <see cref="IRequiredAttachments"/> interlock blocked.
    /// </summary>
    public const string RequiredMissing = "ATTACHMENTS_REQUIRED_MISSING";
}
