using Codelab.Security.PublicIds;

namespace Starter.Modules.ServiceRequests.Api;

/// <summary>
/// The family of service request record ids on the wire (<c>PublicId&lt;ServiceRequestRecordIds&gt;</c>): the one record
/// family of the Records module (its <c>RecordsIds</c>, label <c>records</c>), so the token the create endpoint returns
/// is the token of the generic record, queue and transition endpoints too.
/// </summary>
public sealed class ServiceRequestRecordIds : IIdDomain
{
    /// <summary>The domain label of every record id (equal to the Records module's; part of the wire format; never rename).</summary>
    public const string DomainLabel = "records";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
