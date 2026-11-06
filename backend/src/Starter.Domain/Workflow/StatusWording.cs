namespace Starter.Domain.Workflow;

/// <summary>The i18n keys that word one status for each <see cref="WorkflowAudience"/>; the client translates them.</summary>
/// <param name="RequesterKey">The key the requester screens show.</param>
/// <param name="StaffKey">The key the staff screens show.</param>
/// <param name="AdminKey">The key the admin oversight view shows.</param>
public sealed record StatusWording(string RequesterKey, string StaffKey, string AdminKey)
{
    /// <summary>The key for <paramref name="audience"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="audience"/> is not a defined member.</exception>
    public string For(WorkflowAudience audience) => audience switch
    {
        WorkflowAudience.Requester => RequesterKey,
        WorkflowAudience.Staff => StaffKey,
        WorkflowAudience.Admin => AdminKey,
        _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, "Not a workflow audience."),
    };
}
