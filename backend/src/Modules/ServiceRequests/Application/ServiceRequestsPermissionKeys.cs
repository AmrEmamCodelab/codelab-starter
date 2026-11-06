namespace Starter.Modules.ServiceRequests.Application;

/// <summary>
/// The permission keys the ServiceRequests module declares (format: <c>Starter.Application.Authorization.PermissionKeyFormat</c>).
/// Guard endpoints and transitions with these constants, never with literals. Add one with
/// <c>node tools/scaffold/new-permission.mjs --module ServiceRequests --action &lt;kebab&gt;</c>.
/// </summary>
public static class ServiceRequestsPermissionKeys
{
    /// <summary>File service requests: create, fill in and submit a draft, resubmit and cancel one's own request.</summary>
    public const string Create = "service-requests.create";

    /// <summary>See one's own service requests (list, detail, timeline, files).</summary>
    public const string ViewOwn = "service-requests.view-own";

    /// <summary>See the service request work queue and the requests in it (the record type's queue permission).</summary>
    public const string Queue = "service-requests.queue";

    /// <summary>Start the review of a submitted or resubmitted service request.</summary>
    public const string Review = "service-requests.review";

    /// <summary>Decide a service request under review: approve, reject or return it.</summary>
    public const string Decide = "service-requests.decide";

    // scaffold:permission-keys
}
