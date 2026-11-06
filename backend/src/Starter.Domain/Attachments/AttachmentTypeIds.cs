namespace Starter.Domain.Attachments;

/// <summary>
/// The ids of the attachment types (<c>attachments.ServiceAttachments</c>), exactly as the migrations that seed them
/// write them. Compare and assign type ids only through these constants; never renumber one (stored links hold the
/// ids). Add a type with the recipe <c>docs/recipes/add-attachment-type.md</c>.
/// </summary>
public static class AttachmentTypeIds
{
    /// <summary>The Development demo document (<see cref="AttachmentTypeKeys.DemoDocument"/>, record type <see cref="AttachableRecordTypes.Demo"/>).</summary>
    public const int DemoDocument = 1;

    /// <summary>
    /// The sample's required request document (<see cref="AttachmentTypeKeys.ServiceRequestDocument"/>, record type
    /// <see cref="AttachableRecordTypes.ServiceRequest"/>; seeded by the Attachments migration
    /// AddServiceRequestAttachmentTypes). Stays when the sample module is removed: the seeded row stays too.
    /// </summary>
    public const int ServiceRequestDocument = 2;

    /// <summary>
    /// The sample's optional supporting files (<see cref="AttachmentTypeKeys.ServiceRequestSupporting"/>, record type
    /// <see cref="AttachableRecordTypes.ServiceRequest"/>; seeded by AddServiceRequestAttachmentTypes).
    /// </summary>
    public const int ServiceRequestSupporting = 3;
}
