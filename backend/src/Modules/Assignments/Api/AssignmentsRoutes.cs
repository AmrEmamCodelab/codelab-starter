using Codelab.Security.PublicIds;
using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Assignments.Api;

/// <summary>The Assignments routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class AssignmentsRoutes
{
    /// <summary>The root of every queue endpoint: <c>api/queues</c>.</summary>
    public const string Root = ApiRoutes.Prefix + ApiRoutes.Separator + "queues";

    /// <summary>The name of the route parameter that carries a record type key (checked by the use case; unknown answers 404).</summary>
    public const string RecordTypeParameter = "recordType";

    /// <summary>The name of the route parameter that carries a record's PublicId (family <c>records</c>).</summary>
    public const string IdParameter = "id";

    /// <summary>The queue of one record type: <c>{recordType}</c> under <see cref="Root"/>.</summary>
    public const string Queue = "{" + RecordTypeParameter + "}";

    /// <summary>One record of a queue: <c>{recordType}/{id}</c>; the id is a PublicId, never a raw number.</summary>
    public const string Item = Queue + ApiRoutes.Separator + "{" + IdParameter + ":" + PublicIdRouteConstraint.ConstraintName + "}";

    /// <summary><c>POST {recordType}/{id}/claim</c>: the caller takes the record.</summary>
    public const string Claim = Item + ApiRoutes.Separator + "claim";

    /// <summary><c>POST {recordType}/{id}/release</c>: the caller gives the record back.</summary>
    public const string Release = Item + ApiRoutes.Separator + "release";

    /// <summary><c>POST {recordType}/{id}/assign</c>: hand the record to another user.</summary>
    public const string Assign = Item + ApiRoutes.Separator + "assign";

    /// <summary>The JSON response content type.</summary>
    public const string JsonContentType = "application/json";
}
