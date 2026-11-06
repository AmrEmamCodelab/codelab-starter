using System.Reflection;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Starter.Infrastructure.Authorization;
using Starter.Modules.Attachments.Api;
using Starter.Modules.Attachments.Application;

namespace Starter.Modules.Attachments.Tests.Api;

/// <summary>Every endpoint's method, route and permission as the contract states them, read from the attributes.</summary>
public sealed class AttachmentsEndpointShapeTests
{
    public static TheoryData<string, string, string, string> Endpoints => new()
    {
        { nameof(AttachmentsController.ListTypesAsync), "GET", "api/attachments/types", AttachmentsPermissionKeys.View },
        { nameof(AttachmentsController.RemoveAsync), "DELETE", "api/attachments/{id:publicid}", AttachmentsPermissionKeys.Remove },
        { nameof(AttachmentsController.GetDemoRecord), "GET", "api/attachments/demo-record", AttachmentsPermissionKeys.View },
        { nameof(RecordAttachmentsController.ListAsync), "GET", "api/records/{recordType}/{recordId:publicid}/attachments", AttachmentsPermissionKeys.View },
        { nameof(RecordAttachmentsController.IssueTicketAsync), "POST", "api/records/{recordType}/{recordId:publicid}/attachments/upload-ticket", AttachmentsPermissionKeys.Upload },
        { nameof(RecordAttachmentsController.LinkAsync), "POST", "api/records/{recordType}/{recordId:publicid}/attachments", AttachmentsPermissionKeys.Upload },
        { nameof(AttachmentContentController.DownloadAsync), "GET", "api/attachments/{id:publicid}/content", AttachmentsPermissionKeys.View },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void Each_endpoint_has_its_method_route_and_permission(string action, string method, string route, string permission)
    {
        var (controller, info) = Find(action);
        var http = Assert.Single(info.GetCustomAttributes().OfType<HttpMethodAttribute>());
        var root = controller.GetCustomAttribute<RouteAttribute>()!.Template;

        Assert.Equal([method], http.HttpMethods);
        Assert.Equal(route, string.IsNullOrEmpty(http.Template) ? root : root + "/" + http.Template);
        Assert.Equal(permission, Assert.Single(info.GetCustomAttributes<HasPermissionAttribute>()).Permission);
    }

    [Fact]
    public void Only_the_download_has_an_unsigned_response()
    {
        var unsigned = Controllers()
            .SelectMany(controller => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<UnsignedResponseAttribute>() is not null)
            .Select(method => method.Name);

        Assert.Equal([nameof(AttachmentContentController.DownloadAsync)], unsigned);
        Assert.Equal(AttachmentsSigning.FileDownloadReason, typeof(AttachmentContentController)
            .GetMethod(nameof(AttachmentContentController.DownloadAsync))!.GetCustomAttribute<UnsignedResponseAttribute>()!.Why);
    }

    [Fact]
    public void The_id_labels_point_at_the_one_family_of_each_kind()
    {
        Assert.Equal(AttachmentIds.DomainLabel, AttachmentsIds.DomainLabel);
        Assert.Equal(AttachableRecordIds.DomainLabel, AttachmentsIds.RecordDomainLabel);
        Assert.Empty(typeof(AttachmentsIds).GetInterfaces());
    }

    private static Type[] Controllers() => [typeof(AttachmentsController), typeof(RecordAttachmentsController), typeof(AttachmentContentController)];

    private static (Type Controller, MethodInfo Action) Find(string action)
    {
        var (controller, method) = Controllers()
            .Select(type => (Type: type, Method: type.GetMethod(action, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
            .Single(pair => pair.Method is not null);
        return (controller, method!);
    }
}
