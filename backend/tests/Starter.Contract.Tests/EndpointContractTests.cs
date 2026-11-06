using Codelab.Security.PublicIds;
using Codelab.Testing.Contracts;
using Starter.Application.Validation;
using Starter.Contract.Tests.Support;

namespace Starter.Contract.Tests;

/// <summary>
/// Rule <c>base.endpoint-contract</c> over the real host: permissions, PublicIds and validators. Modules are not
/// listed; the host discovers them, so a new module is covered the moment Starter.Api references it.
/// </summary>
[Collection(ContractHostGroup.Name)]
public sealed class EndpointContractTests(ContractHost host)
{
    [Fact]
    public void TheHostMapsEveryModuleEndpoint() => Assert.NotEmpty(EndpointDescriptions.Routable(host.Endpoints));

    [Fact]
    public void AllEndpointsHavePermission() =>
        EndpointAccessContract.Check(host.Endpoints, EndpointAllowlist.Entries, host.PresentOwners).ThrowIfFailed();

    [Fact]
    public void AllAnonymousWritesAreRateLimited() =>
        AnonymousWriteRateLimitContract.Check(host.Endpoints).ThrowIfFailed();

    [Fact]
    public void AllIdsArePublicId() =>
        PublicIdContract.AllIdsArePublicId(host.Endpoints, IsPublicId, EndpointAllowlist.NumericLookupIdMembers).ThrowIfFailed();

    [Fact]
    public void AllWritesHaveValidator()
    {
        var assemblies = host.Modules.Assemblies.Append(typeof(Program).Assembly).ToArray();

        EndpointContracts.AllEndpointsHaveValidator(host.Endpoints, EndpointContracts.ValidatorsIn(typeof(IValidator<>), assemblies)).ThrowIfFailed();
    }

    [Fact]
    public void AtLeastOneWriteTakesABody() =>
        Assert.Contains(EndpointDescriptions.Routable(host.Endpoints), endpoint => EndpointDescriptions.RequestBodyTypes(endpoint).Count > 0);

    private static bool IsPublicId(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PublicId<>);
}
