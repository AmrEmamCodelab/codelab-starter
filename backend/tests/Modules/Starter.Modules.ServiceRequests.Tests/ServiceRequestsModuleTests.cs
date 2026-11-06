using Codelab.Security.PublicIds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Modules.ServiceRequests.Api;
using Starter.Modules.ServiceRequests.Application;
using Starter.Modules.ServiceRequests.Application.Drafts;
using Starter.Modules.ServiceRequests.Application.Workflow;
using Starter.Modules.ServiceRequests.Domain;
using Starter.Modules.ServiceRequests.Infrastructure;
using Starter.Modules.ServiceRequests.Tests.Fakes;

namespace Starter.Modules.ServiceRequests.Tests;

public sealed class ServiceRequestsModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly()
    {
        Assert.Equal("ServiceRequests", new ServiceRequestsModule().Name);
    }

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_and_described()
    {
        var permissions = new ServiceRequestsPermissionCatalogSource().Permissions;

        Assert.NotEmpty(permissions);
        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(permissions.Count, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_declared_key_is_in_the_catalog()
    {
        var declared = typeof(ServiceRequestsPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);
        var catalog = new ServiceRequestsPermissionCatalogSource().Permissions.Select(permission => permission.Key);

        Assert.Equal(declared.Order(StringComparer.Ordinal), catalog.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_permission_keys_are_the_contract_keys()
    {
        Assert.Equal(
            ["service-requests.create", "service-requests.decide", "service-requests.queue", "service-requests.review", "service-requests.view-own"],
            new ServiceRequestsPermissionCatalogSource().Permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Registration_configures_the_engine_for_the_sample()
    {
        var services = new ServiceCollection();

        services.AddServiceRequestsModule(new ConfigurationBuilder().Build());

        AssertRegistered<IPermissionCatalogSource, ServiceRequestsPermissionCatalogSource>(services);
        AssertRegistered<IRecordWorkflowDefinition, ServiceRequestWorkflowDefinition>(services);
        AssertRegistered<IRecordTypeRegistrationSource, ServiceRequestRecordTypeSource>(services);
        AssertRegistered<ISlaPolicySource, ServiceRequestSlaPolicySource>(services);
        AssertRegistered<IRecordSubmitValidator, ServiceRequestSubmitValidator>(services);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRecordWorkflowCatalogs));
    }

    [Fact]
    public void Registering_twice_adds_each_engine_source_once()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddServiceRequestsModule(configuration);
        services.AddServiceRequestsModule(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRecordWorkflowDefinition));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRecordTypeRegistrationSource));
    }

    [Fact]
    public void The_record_type_registration_is_the_contract_one()
    {
        var registration = Assert.Single(new ServiceRequestRecordTypeSource().Registrations);

        Assert.Equal(
            new RecordTypeRegistration(
                RecordType: "service-request",
                NumberPrefix: "SR",
                QueuePermissionKey: ServiceRequestsPermissionKeys.Queue,
                ViewOwnPermissionKey: ServiceRequestsPermissionKeys.ViewOwn,
                AttachableRecordType: "service-request"),
            registration);
    }

    [Fact]
    public void The_sla_is_three_days_on_under_review_with_a_one_day_warning_and_an_escalation()
    {
        var sla = Assert.Single(new ServiceRequestSlaPolicySource().Definitions);

        Assert.Equal(
            (ServiceRequestRecord.RecordType, ServiceRequestStatuses.UnderReview, TimeSpan.FromDays(3), TimeSpan.FromDays(1), ServiceRequestNotificationKeys.SlaEscalation),
            (sla.RecordType, sla.Status, sla.Duration, sla.DueSoonBefore, sla.EscalationTemplateKey));
    }

    [Fact]
    public void The_sla_badge_turns_due_soon_a_day_before_and_breached_after_three_days()
    {
        var sla = ServiceRequestSlaPolicySource.UnderReview;
        var entered = TestValues.At;

        Assert.Equal(SlaState.OnTrack, sla.BadgeAt(entered, entered.AddDays(1)).State);
        Assert.Equal(SlaState.DueSoon, sla.BadgeAt(entered, entered.AddDays(2)).State);
        Assert.Equal(SlaState.Breached, sla.BadgeAt(entered, entered.AddDays(3)).State);
        Assert.Equal(entered.AddDays(3), sla.BadgeAt(entered, entered).DueAt);
    }

    [Fact]
    public void Routes_live_under_the_module_root_and_ids_use_the_shared_record_family()
    {
        Assert.Equal("api/service-requests", ServiceRequestsRoutes.Root);
        Assert.Equal($"{{id:{PublicIdRouteConstraint.ConstraintName}}}/draft", ServiceRequestsRoutes.Draft);
        Assert.Equal("records", ServiceRequestRecordIds.DomainLabel);
        Assert.Equal("lookup-items", ServiceRequestCategoryTokens.DomainLabel);
    }

    [Fact]
    public void The_calendar_reads_today_in_the_app_time_zone()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 22, 30, 0, TimeSpan.Zero));
        var plusThree = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");

        Assert.Equal(new DateOnly(2026, 11, 6), new AppTimeZoneCalendar(clock, plusThree).Today());
        Assert.Equal(new DateOnly(2026, 11, 5), new AppTimeZoneCalendar(clock, TimeZoneInfo.Utc).Today());
    }

    [Fact]
    public void The_status_values_are_frozen()
    {
        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8],
            new[]
            {
                ServiceRequestStatuses.Draft, ServiceRequestStatuses.Submitted, ServiceRequestStatuses.UnderReview, ServiceRequestStatuses.Returned,
                ServiceRequestStatuses.Resubmitted, ServiceRequestStatuses.Approved, ServiceRequestStatuses.Rejected, ServiceRequestStatuses.Cancelled,
            });
        Assert.Equal([1, 2, 3], Enum.GetValues<ServiceRequestDraftStep>().Select(step => (int)step));
    }

    private static void AssertRegistered<TService, TImplementation>(ServiceCollection services) =>
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TService) && descriptor.ImplementationType == typeof(TImplementation));
}
