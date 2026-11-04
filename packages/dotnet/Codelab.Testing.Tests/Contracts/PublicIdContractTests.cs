using Codelab.Testing.Contracts;
using Codelab.Testing.Tests.Fixtures.Endpoints;
using Codelab.Testing.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Codelab.Testing.Tests.Contracts;

public sealed class PublicIdContractTests
{
    private static readonly Func<Type, bool> IsWireId = type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WireId<>);

    [Fact]
    public void Id_members_of_wire_types_must_be_wire_ids_unless_allowlisted()
    {
        var result = PublicIdContract.AllWireTypeIdsArePublicId([typeof(RecordView)], IsWireId, ["RecordView.StatusId"]);

        Assert.Equal(
            ["RecordView.CategoryId is Int32, not a PublicId (or allowlist it as a lookup id)", "LineView.ProductId is Int64, not a PublicId (or allowlist it as a lookup id)"],
            result.Violations);
        Assert.Equal(2, result.Examined);
    }

    [Fact]
    public void Wire_only_types_pass()
    {
        var result = PublicIdContract.AllWireTypeIdsArePublicId([typeof(ItemView), typeof(List<ItemView>)], IsWireId);

        Assert.True(result.IsSuccessful, result.Describe());
        Assert.Equal(1, result.Examined);
    }

    [Fact]
    public async Task Endpoint_routes_and_bodies_are_checked()
    {
        await using var app = EndpointFixtureHost.Build(MapIdEndpoints, withControllers: true);

        var result = PublicIdContract.AllIdsArePublicId(EndpointFixtureHost.EndpointsOf(app), IsWireId, ["RecordView.StatusId"]);

        Assert.Equal(
        [
            "GET /items/{itemId}: route value {itemId} binds to String, not a PublicId",
            "GET /legacy/{legacyId}: route value {legacyId} binds to Int64, not a PublicId",
            $"GET /orders/{FixtureRoutes.OrderById}: route value {{orderId}} is constrained to long (use a PublicId)",
            $"GET /orders/{FixtureRoutes.OrderById}: route value {{orderId}} binds to Int64, not a PublicId",
            $"GET {FixtureRoutes.RecordById}: route value {{recordId}} is constrained to int (use a PublicId)",
            $"GET {FixtureRoutes.RecordById}: route value {{recordId}} binds to Int32, not a PublicId",
            "RecordView.CategoryId is Int32, not a PublicId (or allowlist it as a lookup id)",
            "LineView.ProductId is Int64, not a PublicId (or allowlist it as a lookup id)",
        ], result.Violations);
    }

    [Theory]
    [InlineData("""{ "id": 7 }""", "$.id")]
    [InlineData("""{ "items": [ { "ownerId": "42" } ] }""", "$.items[0].ownerId")]
    [InlineData("""{ "relatedIds": [ "tok", 9 ] }""", "$.relatedIds[1]")]
    [InlineData("""[ { "nested": { "parentId": 3 } } ]""", "$[0].nested.parentId")]
    public void Numeric_ids_in_a_response_body_are_found(string json, string path) =>
        Assert.Equal([path], WireJsonSweep.FindNumericIds(json, []));

    [Fact]
    public void Allowed_keys_tokens_and_non_id_numbers_pass()
    {
        const string json = """{ "id": "AbC-12_x", "statusId": 4, "count": 9, "children": [ { "id": "tok" } ] }""";

        var result = WireJsonSweep.Check(json, "GET /records", ["statusId"]);

        Assert.True(result.IsSuccessful, result.Describe());
        Assert.True(WireJsonSweep.IsIdKey("Id"));
        Assert.False(WireJsonSweep.IsIdKey("identity"));
    }

    [Fact]
    public void Wire_type_helpers_unwrap_collections_and_results()
    {
        Assert.Equal(typeof(int), WireTypes.ElementOf(typeof(int?[])));
        Assert.Equal(typeof(long), WireTypes.ElementOf(typeof(IReadOnlyList<long>)));
        Assert.Equal(typeof(string), WireTypes.ElementOf(typeof(string)));
        Assert.Equal(typeof(ItemView), WireTypes.UnwrapResult(typeof(Task<Microsoft.AspNetCore.Mvc.ActionResult<ItemView>>)));
        Assert.Null(WireTypes.UnwrapResult(typeof(Task<IResult>)));
        Assert.True(WireTypes.IsIdName("RelatedIds"));
        Assert.False(WireTypes.IsIdName("Valid"));
    }

    private static void MapIdEndpoints(WebApplication app)
    {
        app.MapGet(FixtureRoutes.RecordById, (int recordId) => recordId).Produces<RecordView>();
        app.MapGet("/items/{itemId}", (string itemId) => new ItemView(new WireId<ItemView>(itemId), itemId)).Produces<ItemView>();
        app.MapGet("/legacy/{legacyId}", (long legacyId) => legacyId);
    }
}
