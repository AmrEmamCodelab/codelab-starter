using Starter.Application.Authorization;

namespace Starter.Modules.ServiceRequests.Application;

/// <summary>The ServiceRequests module's share of the permission catalog.</summary>
public sealed class ServiceRequestsPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: ServiceRequestsPermissionKeys.Create, Description: "File service requests: create, fill in and submit a draft, resubmit and cancel your own request."),
        new(Key: ServiceRequestsPermissionKeys.ViewOwn, Description: "See your own service requests."),
        new(Key: ServiceRequestsPermissionKeys.Queue, Description: "See the service request work queue and the requests in it."),
        new(Key: ServiceRequestsPermissionKeys.Review, Description: "Start the review of a submitted service request."),
        new(Key: ServiceRequestsPermissionKeys.Decide, Description: "Approve, reject or return a service request under review."),
        // scaffold:permission-catalog
    ];
}
