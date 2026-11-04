using Codelab.Testing.Contracts;
using Codelab.Testing.Tests.Fixtures.Endpoints;
using Codelab.Testing.Tests.Support;

namespace Codelab.Testing.Tests.Contracts;

public sealed class EndpointContractsTests
{
    [Fact]
    public async Task Permission_contract_reports_silent_unlisted_stale_and_wrongly_listed_endpoints()
    {
        await using var app = EndpointFixtureHost.Build(EndpointFixtureHost.MapRecordEndpoints);
        string[] allowlist = ["GET /health", "GET /gone", "GET /records/{id}"];

        var result = EndpointContracts.AllEndpointsHavePermission(EndpointFixtureHost.EndpointsOf(app), typeof(FixturePermissionAttribute), allowlist);

        Assert.Equal(
        [
            "GET /bare: no FixturePermissionAttribute (and not allowlisted as anonymous)",
            "GET /open: anonymous but not in the anonymous allowlist",
            "GET /records/{id}: in the anonymous allowlist but not anonymous (remove the entry)",
            "GET /gone: allowlisted as anonymous but no such endpoint exists (remove the stale entry)",
        ], result.Violations);
        Assert.Equal(6, result.Examined);
    }

    [Fact]
    public async Task Permission_contract_passes_when_every_endpoint_is_gated_or_listed()
    {
        await using var app = EndpointFixtureHost.Build(EndpointFixtureHost.MapRecordEndpoints);
        var endpoints = EndpointFixtureHost.EndpointsOf(app).Where(endpoint => !endpoint.DisplayName!.Contains("bare", StringComparison.Ordinal));

        var result = EndpointContracts.AllEndpointsHavePermission(endpoints, typeof(FixturePermissionAttribute), ["GET /health", "GET /open"]);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public async Task Permission_contract_covers_controller_actions()
    {
        await using var app = EndpointFixtureHost.Build(_ => { }, withControllers: true);

        var result = EndpointContracts.AllEndpointsHavePermission(EndpointFixtureHost.EndpointsOf(app), typeof(FixturePermissionAttribute), []);

        Assert.True(result.IsSuccessful, result.Describe());
        Assert.Equal(2, result.Examined);
    }

    [Fact]
    public async Task Validator_contract_reports_minimal_api_and_controller_bodies_without_a_validator()
    {
        await using var app = EndpointFixtureHost.Build(EndpointFixtureHost.MapRecordEndpoints, withControllers: true);
        var hasValidator = EndpointContracts.ValidatorsIn(typeof(IFixtureValidator<>), typeof(RecordInputValidator).Assembly);

        var result = EndpointContracts.AllEndpointsHaveValidator(EndpointFixtureHost.EndpointsOf(app), hasValidator);

        Assert.Equal(
            ["POST /notes: request body NoteInput has no validator", "POST /orders: request body OrderInput has no validator"],
            result.Violations);
    }

    [Fact]
    public async Task Validator_contract_skips_exempt_endpoints()
    {
        await using var app = EndpointFixtureHost.Build(EndpointFixtureHost.MapRecordEndpoints);
        var hasValidator = EndpointContracts.ValidatorsIn(typeof(IFixtureValidator<>), typeof(RecordInputValidator).Assembly);

        var result = EndpointContracts.AllEndpointsHaveValidator(EndpointFixtureHost.EndpointsOf(app), hasValidator, ["POST /notes"]);

        Assert.True(result.IsSuccessful, result.Describe());
        Assert.Equal(5, result.Examined);
    }

    [Fact]
    public void Validator_lookup_needs_an_open_generic_interface()
    {
        Assert.Throws<ArgumentException>(() => EndpointContracts.ValidatorsIn(typeof(IFixtureValidator<RecordInput>)));
        Assert.Throws<ArgumentException>(() => EndpointContracts.ValidatorsIn(typeof(Dictionary<,>)));
    }

    [Fact]
    public async Task Endpoint_descriptions_give_keys_methods_and_bodies()
    {
        await using var app = EndpointFixtureHost.Build(EndpointFixtureHost.MapRecordEndpoints, withControllers: true);

        var routable = EndpointDescriptions.Routable(EndpointFixtureHost.EndpointsOf(app));
        var create = routable.Single(endpoint => EndpointDescriptions.KeyOf(endpoint) == "POST /orders");

        Assert.Contains(routable, endpoint => EndpointDescriptions.KeyOf(endpoint) == "GET /orders/" + FixtureRoutes.OrderById);
        Assert.Equal([typeof(OrderInput)], EndpointDescriptions.RequestBodyTypes(create));
        Assert.Equal([typeof(ItemView)], EndpointDescriptions.ResponseTypes(create));
        Assert.Equal(typeof(OrderInput), EndpointDescriptions.HandlerParameters(create)["input"]);
    }
}
