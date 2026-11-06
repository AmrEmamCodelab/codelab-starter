using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Authorization;
using Starter.Application.Workflow;
using Starter.Contract.Tests.Support;

namespace Starter.Contract.Tests.Workflow;

/// <summary>
/// Exit criterion "workflow conformance" over the real host: every transition in every registered catalog has a
/// permission, a wording for each audience and a notification template decision. Definitions are discovered by
/// reflection over the host's module assemblies, so the test holds for zero or more record types and names none.
/// </summary>
[Collection(ContractHostGroup.Name)]
public sealed class WorkflowConformanceContractTests(ContractHost host)
{
    [Fact]
    public void EveryTransitionInEveryRegisteredCatalogHasAPermissionAWordingForEachAudienceAndANotificationDecision()
    {
        var services = host.Factory.Services;
        var registered = services.GetServices<IRecordWorkflowDefinition>().ToList();
        var declared = WorkflowConformance.DefinitionTypesIn(host.Modules.Assemblies);
        var knownKeys = services.GetServices<IPermissionCatalogSource>()
            .SelectMany(source => source.Permissions)
            .Select(permission => permission.Key)
            .ToHashSet(StringComparer.Ordinal);

        var breaches = WorkflowConformance.Check(registered, declared, knownKeys);

        Assert.True(breaches.Count == 0, string.Join(Environment.NewLine, breaches));
    }

    [Fact]
    public void EveryRegisteredCatalogPassesTheStartupValidator()
    {
        var services = host.Factory.Services;
        var knownKeys = services.GetServices<IPermissionCatalogSource>()
            .SelectMany(source => source.Permissions)
            .Select(permission => permission.Key)
            .ToHashSet(StringComparer.Ordinal);

        var violations = RecordWorkflowValidation.ValidateAll(services.GetServices<IRecordWorkflowDefinition>(), knownKeys);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void EveryRegisteredCatalogIsServedByTheRegistry()
    {
        var services = host.Factory.Services;
        var catalogs = services.GetService<IRecordWorkflowCatalogs>();
        var registered = services.GetServices<IRecordWorkflowDefinition>().ToList();
        if (registered.Count == 0)
        {
            return;
        }

        Assert.NotNull(catalogs);
        Assert.All(registered, definition => Assert.Same(definition.Catalog, catalogs.Find(definition.RecordType)));
    }
}
