namespace Starter.Domain.Attachments;

/// <summary>
/// The ids of the attachment types (<c>attachments.ServiceAttachments</c>), exactly as the migration or the declaring
/// module (IAttachmentTypeSeedSource) writes them. Compare and assign type ids only through these constants; never renumber one (stored links hold the
/// ids). Add a type with the recipe <c>docs/recipes/add-attachment-type.md</c>.
/// </summary>
public static class AttachmentTypeIds
{
    /// <summary>The Development demo document (<see cref="AttachmentTypeKeys.DemoDocument"/>, record type <see cref="AttachableRecordTypes.Demo"/>).</summary>
    public const int DemoDocument = 1;

    /// <summary>
    /// The sample's required request document (<see cref="AttachmentTypeKeys.ServiceRequestDocument"/>, record type
    /// <see cref="AttachableRecordTypes.ServiceRequest"/>; declared by the ServiceRequests module,
    /// ServiceRequestAttachmentTypes, and written by the Attachments start-up synchronizer). The id stays reserved when the
    /// sample module is removed; never reuse it.
    /// </summary>
    public const int ServiceRequestDocument = 2;

    /// <summary>
    /// The sample's optional supporting files (<see cref="AttachmentTypeKeys.ServiceRequestSupporting"/>, record type
    /// <see cref="AttachableRecordTypes.ServiceRequest"/>; declared by ServiceRequestAttachmentTypes).
    /// </summary>
    public const int ServiceRequestSupporting = 3;
}
