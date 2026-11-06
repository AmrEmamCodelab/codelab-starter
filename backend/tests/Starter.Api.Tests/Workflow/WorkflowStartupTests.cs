using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Api.Tests.Support;
using Starter.Application.Authorization;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Infrastructure.Features;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Workflow;
using Starter.Workflow.Testing;

namespace Starter.Api.Tests.Workflow;

/// <summary>
/// Workflow conformance at boot (exit criterion): a host whose module registers a deliberately broken catalog does
/// not start and lists every violation; a valid catalog starts and is served by the registry and the facade.
/// </summary>
[Collection(HostTestGroup.Name)]
public sealed class WorkflowStartupTests
{
    [Fact]
    public async Task A_host_with_a_broken_catalog_fails_to_start_and_lists_every_violation()
    {
        var module = new WorkflowProbeModule(services => services.AddRecordWorkflow<CombinedBrokenDefinition>());

        var failure = await Assert.ThrowsAsync<WorkflowCatalogInvalidException>(() => KernelTestHost.StartAsync(ModuleCatalog.FromModules([module])));

        Assert.Equal(
            [CatalogViolationKind.UnreachableState, CatalogViolationKind.MissingPermission, CatalogViolationKind.ReasonFieldMissing],
            failure.Violations.Select(violation => violation.Kind).Order());
        Assert.Contains("UnreachableState status orphan (9)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("MissingPermission take (2 -> 3)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("ReasonFieldMissing return (3 -> 4)", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_check_runs_before_any_other_hosted_service_starts()
    {
        var journal = new StartJournal();
        var module = new WorkflowProbeModule(services =>
        {
            services.AddSingleton(journal);
            services.AddHostedService<JournalingService>();
            services.AddRecordWorkflow<CombinedBrokenDefinition>();
        });

        await Assert.ThrowsAsync<WorkflowCatalogInvalidException>(() => KernelTestHost.StartAsync(ModuleCatalog.FromModules([module])));

        Assert.False(journal.Started);
    }

    [Fact]
    public async Task A_catalog_whose_permissions_no_module_declares_fails_to_start()
    {
        var module = new WorkflowProbeModule(services => services.AddRecordWorkflow<TestRecordWorkflowDefinition>(), declarePermissions: false);

        var failure = await Assert.ThrowsAsync<WorkflowCatalogInvalidException>(() => KernelTestHost.StartAsync(ModuleCatalog.FromModules([module])));

        Assert.All(failure.Violations, violation => Assert.Equal(CatalogViolationKind.UnknownPermission, violation.Kind));
    }

    [Fact]
    public async Task Two_definitions_of_one_record_type_fail_to_start()
    {
        var module = new WorkflowProbeModule(services =>
        {
            services.AddRecordWorkflow<TestRecordWorkflowDefinition>();
            services.AddRecordWorkflow<SecondTestRecordDefinition>();
        });

        var failure = await Assert.ThrowsAsync<WorkflowCatalogInvalidException>(() => KernelTestHost.StartAsync(ModuleCatalog.FromModules([module])));

        Assert.Equal(CatalogViolationKind.RecordTypeConflict, Assert.Single(failure.Violations).Kind);
    }

    [Fact]
    public async Task A_valid_catalog_starts_and_is_served_by_the_registry_and_the_facade()
    {
        var module = new WorkflowProbeModule(services =>
        {
            services.AddRecordWorkflow<TestRecordWorkflowDefinition>();
            services.AddRecordWorkflow<TestRecordWorkflowDefinition>();
            services.AddSingleton<IWorkflowRecordReader<ProbeRecord>, ProbeRecordReader>();
        });

        await using var app = await KernelTestHost.StartAsync(ModuleCatalog.FromModules([module]));
        await using var scope = app.Services.CreateAsyncScope();

        Assert.Same(TestRecordWorkflow.Catalog, Assert.Single(app.Services.GetRequiredService<IRecordWorkflowCatalogs>().All));
        var workflow = scope.ServiceProvider.GetRequiredService<IRecordWorkflow<ProbeRecord>>();
        var actor = new WorkflowActor(UserId: ProbeRecord.Owner, TransitionSide.Requester, TestRecordPermissionKeys.All);
        Assert.Contains(workflow.AllowedActions(new ProbeRecord(TestRecordStatuses.Draft), actor), action => action.ActionKey == TestRecordActions.Submit);
        Assert.Single(app.Services.GetServices<IHostedService>().OfType<WorkflowCatalogStartupCheck>());
    }

    [Fact]
    public async Task A_host_with_the_workflow_kernel_and_zero_catalogs_starts()
    {
        var module = new WorkflowProbeModule(services => services.AddRecordWorkflowKernel());

        await using var app = await KernelTestHost.StartAsync(ModuleCatalog.FromModules([module]));

        Assert.Empty(app.Services.GetRequiredService<IRecordWorkflowCatalogs>().All);
    }

    [Fact]
    public void The_check_can_run_outside_a_host()
    {
        var check = new WorkflowCatalogStartupCheck([new CombinedBrokenDefinition()], [new TestRecordPermissions()]);

        Assert.Equal(3, Assert.Throws<WorkflowCatalogInvalidException>(check.Check).Violations.Count);
        new WorkflowCatalogStartupCheck([new TestRecordWorkflowDefinition()], [new TestRecordPermissions()]).Check();
    }

    private sealed class WorkflowProbeModule(Action<IServiceCollection> register, bool declarePermissions = true) : IModule
    {
        public string Name => "WorkflowProbe";

        public void AddModule(IServiceCollection services, IConfiguration configuration)
        {
            // This assembly is the module assembly, so its probe controllers are application parts and their feature
            // flag must be declared (as ProbeModule does).
            services.AddSingleton<IFeatureFlagSource, ProbeFlags>();
            if (declarePermissions)
            {
                services.AddSingleton<IPermissionCatalogSource, TestRecordPermissions>();
            }

            register(services);
        }

        public void UseModule(IEndpointRouteBuilder endpoints)
        {
        }
    }

    private sealed class TestRecordPermissions : IPermissionCatalogSource
    {
        public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
            [.. TestRecordPermissionKeys.All.Select(key => new PermissionDefinition(key, "Test record permission."))];
    }

    private sealed class CombinedBrokenDefinition : IRecordWorkflowDefinition
    {
        public string RecordType => TestRecordWorkflow.RecordType;

        public TransitionCatalog Catalog => BrokenCatalogs.Combined;
    }

    private sealed class SecondTestRecordDefinition : IRecordWorkflowDefinition
    {
        public string RecordType => TestRecordWorkflow.RecordType;

        public TransitionCatalog Catalog => TestRecordWorkflow.Catalog;
    }

    private sealed record ProbeRecord(int Status)
    {
        public const long Owner = 5;
    }

    private sealed class ProbeRecordReader : IWorkflowRecordReader<ProbeRecord>
    {
        public string RecordTypeOf(ProbeRecord record) => TestRecordWorkflow.RecordType;

        public int StatusOf(ProbeRecord record) => record.Status;

        public long OwnerUserIdOf(ProbeRecord record) => ProbeRecord.Owner;
    }

    private sealed class StartJournal
    {
        public bool Started { get; set; }
    }

    private sealed class JournalingService(StartJournal journal) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            journal.Started = true;
            return Task.CompletedTask;
        }
    }
}
