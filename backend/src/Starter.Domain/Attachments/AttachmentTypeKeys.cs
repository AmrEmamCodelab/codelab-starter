namespace Starter.Domain.Attachments;

/// <summary>
/// The keys of the attachment types (kebab-case, unique; the <c>typeKey</c> the client sends). Each key belongs to the
/// type with the same-named <see cref="AttachmentTypeIds"/> constant. Compare and request types only through these
/// constants.
/// </summary>
public static class AttachmentTypeKeys
{
    /// <summary>The Development demo document (<see cref="AttachmentTypeIds.DemoDocument"/>).</summary>
    public const string DemoDocument = "demo-document";

    /// <summary>The sample's required request document (<see cref="AttachmentTypeIds.ServiceRequestDocument"/>).</summary>
    public const string ServiceRequestDocument = "service-request-document";

    /// <summary>The sample's optional supporting files (<see cref="AttachmentTypeIds.ServiceRequestSupporting"/>).</summary>
    public const string ServiceRequestSupporting = "service-request-supporting";
}
