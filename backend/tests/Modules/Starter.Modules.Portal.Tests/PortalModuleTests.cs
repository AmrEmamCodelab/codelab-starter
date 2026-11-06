using System.Reflection;
using Codelab.Security.RateLimiting;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Localization;
using Starter.Infrastructure.Authorization;
using Starter.Modules.Portal.Api;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Contact;
using Starter.Modules.Portal.Application.Content;
using Starter.Modules.Portal.Infrastructure;
using Starter.Modules.Portal.Infrastructure.Options;

namespace Starter.Modules.Portal.Tests;

public sealed class PortalModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly()
    {
        Assert.Equal("Portal", new PortalModule().Name);
    }

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_described_and_declared()
    {
        var permissions = new PortalPermissionCatalogSource().Permissions;
        var declared = typeof(PortalPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);

        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(declared.Order(StringComparer.Ordinal), permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Registration_declares_the_catalog_the_validators_and_the_use_cases()
    {
        var services = new ServiceCollection();

        services.AddPortalModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(PortalPermissionCatalogSource));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<SubmitContactMessageCommand>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<ReplacePortalContentCommand>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(SubmitContactMessage));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ReplacePortalContent));
    }

    [Fact]
    public void The_inbox_comes_from_the_options()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{PortalContactOptions.SectionName}:{nameof(PortalContactOptions.InboxEmail)}"] = " team@example.invalid ",
            [$"{PortalContactOptions.SectionName}:{nameof(PortalContactOptions.InboxLanguage)}"] = "2",
        };
        var services = new ServiceCollection();
        services.AddPortalModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var provider = services.BuildServiceProvider();

        var inbox = provider.GetRequiredService<ContactInbox>();

        Assert.Equal("team@example.invalid", inbox.EmailAddress);
        Assert.Equal(AppLanguage.English, inbox.Language);
    }

    [Fact]
    public void A_missing_inbox_is_refused_when_the_options_are_read()
    {
        var services = new ServiceCollection();
        services.AddPortalModule(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PortalContactOptions>>().Value);
    }

    [Fact]
    public void The_contact_form_is_anonymous_rate_limited_and_exempt_from_signing()
    {
        var controller = typeof(PortalContactController);
        var action = controller.GetMethod(nameof(PortalContactController.SubmitAsync))!;

        Assert.Equal(ClientRateLimitOptions.PolicyName, controller.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.NotNull(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal(SignatureExemption.WholeRequest, action.GetCustomAttribute<SignatureExemptAttribute>()!.Scope);
        Assert.Null(controller.GetCustomAttribute<DisableRateLimitingAttribute>());
    }

    [Fact]
    public void The_admin_editor_needs_the_view_and_manage_permissions()
    {
        var controller = typeof(AdminPortalContentController);

        Assert.Equal(PortalPermissionKeys.ContentView, controller.GetMethod(nameof(AdminPortalContentController.GetAsync))!.GetCustomAttribute<HasPermissionAttribute>()!.Permission);
        Assert.Equal(PortalPermissionKeys.ContentManage, controller.GetMethod(nameof(AdminPortalContentController.ReplaceAsync))!.GetCustomAttribute<HasPermissionAttribute>()!.Permission);
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void A_receipt_is_answered_201_and_a_refusal_as_its_problem()
    {
        var created = Outcome.Success(new ContactReceipt("CU-2026-001")).ToCreatedResult(ContactReceiptResponse.From);
        var refused = Outcome.Refused<ContactReceipt>(Refusal.NotFound(PortalErrorCodes.PageNotFound)).ToCreatedResult(ContactReceiptResponse.From);

        var body = Assert.IsType<ObjectResult>(created.Result);
        Assert.Equal(StatusCodes.Status201Created, body.StatusCode);
        Assert.Equal(new ContactReceiptResponse("CU-2026-001"), body.Value);
        Assert.IsNotType<ObjectResult>(refused.Result, exactMatch: true);
    }
}
