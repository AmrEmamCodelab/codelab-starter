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
}
