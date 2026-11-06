using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Modules.Assignments.Api;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Application.Commands;
using Starter.Modules.Assignments.Application.Queues;
using Starter.Modules.Assignments.Infrastructure;

namespace Starter.Modules.Assignments.Tests;

public sealed class AssignmentsModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly()
    {
        Assert.Equal("Assignments", new AssignmentsModule().Name);
    }

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_and_described()
    {
        var permissions = new AssignmentsPermissionCatalogSource().Permissions;

        Assert.NotEmpty(permissions);
        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(permissions.Count, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_declared_key_is_in_the_catalog()
    {
        var declared = typeof(AssignmentsPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);
        var catalog = new AssignmentsPermissionCatalogSource().Permissions.Select(permission => permission.Key);

        Assert.Equal(declared.Order(StringComparer.Ordinal), catalog.Order(StringComparer.Ordinal));
        Assert.Equal(["assignments.assign", "assignments.work"], catalog.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Registration_declares_the_catalog_source_the_kernel_ports_and_the_fail_closed_eligibility()
    {
        var services = new ServiceCollection();

        services.AddAssignmentsModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(AssignmentsPermissionCatalogSource));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRecordAssignmentReader) && descriptor.ImplementationType == typeof(RecordAssignmentReader));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRecordTransitionObserver) && descriptor.ImplementationType == typeof(CloseAssignmentOnTerminal));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAssigneeEligibility) && descriptor.ImplementationType == typeof(UnresolvedAssigneeEligibility));
    }

    [Fact]
    public void A_host_eligibility_registered_first_wins_over_the_default()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAssigneeEligibility, Fakes.FakeAssigneeEligibility>();

        services.AddAssignmentsModule(new ConfigurationBuilder().Build());

        Assert.Equal(typeof(Fakes.FakeAssigneeEligibility), Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IAssigneeEligibility)).ImplementationType);
    }

    [Fact]
    public async Task The_default_eligibility_refuses_everyone()
    {
        var eligibility = new UnresolvedAssigneeEligibility(NullLogger<UnresolvedAssigneeEligibility>.Instance);

        var decision = await eligibility.ScopeForUserAsync(userId: 10, Fakes.TestRecordType.QueuePermission, CancellationToken.None);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Routes_live_under_the_queues_root_and_ids_use_the_shared_labels()
    {
        Assert.Equal("api/queues", AssignmentsRoutes.Root);
        Assert.Equal("{recordType}/{id:publicid}/claim", AssignmentsRoutes.Claim);
        Assert.Equal("{recordType}/{id:publicid}/release", AssignmentsRoutes.Release);
        Assert.Equal("{recordType}/{id:publicid}/assign", AssignmentsRoutes.Assign);
        Assert.Equal("records", QueueRecordIds.DomainLabel);
        Assert.Equal("users", AssigneeUserIds.DomainLabel);
    }
}
