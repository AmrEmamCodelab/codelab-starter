using Codelab.Security.Signing;
using Codelab.Testing.Contracts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Starter.Contract.Tests.Support;
using Starter.Infrastructure.Authorization;
using Starter.Modules.Attachments.Api;
using Starter.Modules.Attachments.Application;

namespace Starter.Contract.Tests.Attachments;

/// <summary>
/// The Attachments endpoints as the real host maps them: exactly the seven contract endpoints, each guarded by an
/// <c>attachments.*</c> permission (never anonymous, never authenticated-only), and only the file download with an
/// unsigned response body. The generic contracts (PublicIds, validators, problem+json) run in
/// <see cref="EndpointContractTests"/> and <see cref="ProblemJsonContractTests"/>.
/// </summary>
[Collection(ContractHostGroup.Name)]
public sealed class AttachmentsEndpointContractTests(ContractHost host)
{
    private const string DownloadKey = "GET /api/attachments/{id:publicid}/content";

    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["GET /api/attachments/types"] = AttachmentsPermissionKeys.View,
        ["GET /api/attachments/demo-record"] = AttachmentsPermissionKeys.View,
        [DownloadKey] = AttachmentsPermissionKeys.View,
        ["DELETE /api/attachments/{id:publicid}"] = AttachmentsPermissionKeys.Remove,
        ["GET /api/records/{recordType}/{recordId:publicid}/attachments"] = AttachmentsPermissionKeys.View,
        ["POST /api/records/{recordType}/{recordId:publicid}/attachments"] = AttachmentsPermissionKeys.Upload,
        ["POST /api/records/{recordType}/{recordId:publicid}/attachments/upload-ticket"] = AttachmentsPermissionKeys.Upload,
    };

    [Fact]
    public void The_host_maps_exactly_the_contract_endpoints_with_their_permissions()
    {
        var mapped = AttachmentsEndpoints().ToDictionary(
            EndpointDescriptions.KeyOf,
            endpoint => string.Join(",", endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Select(guard => guard.Permission)),
            StringComparer.Ordinal);

        Assert.Equal(Expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), mapped.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void Only_the_download_leaves_its_body_unsigned()
    {
        var unsigned = AttachmentsEndpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<UnsignedResponseAttribute>() is not null)
            .Select(EndpointDescriptions.KeyOf);

        Assert.Equal([DownloadKey], unsigned);
    }

    private IEnumerable<RouteEndpoint> AttachmentsEndpoints() =>
        EndpointDescriptions.Routable(host.Endpoints).Where(endpoint =>
            endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly
            == typeof(AttachmentsController).Assembly);
}
