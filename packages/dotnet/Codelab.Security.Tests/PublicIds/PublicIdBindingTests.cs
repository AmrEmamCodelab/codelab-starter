using System.Net;
using Codelab.Security.PublicIds;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Codelab.Security.Tests.PublicIds;

/// <summary>PublicId binding through a real host: minimal APIs (TryParse), MVC (model binder), the route constraint and OpenAPI.</summary>
[Collection(SharedCodecGroup.Name)]
public sealed class PublicIdBindingTests : IAsyncLifetime
{
    private SecurityTestHost _host = null!;

    public async Task InitializeAsync()
    {
        var options = new TestHostOptions
        {
            UseMvc = true,
            ConfigureServices = services => services.AddOpenApi(openApi => openApi.AddCodelabPublicIds()),
            MapEndpoints = Map,
        };
        foreach (var (key, value) in SecurityTestHost.BaseOptions().Settings)
        {
            options.Settings[key] = value;
        }

        _host = await SecurityTestHost.StartAsync(options);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("/minimal/records/{0}")]
    [InlineData("/minimal/records?id={0}")]
    [InlineData("/mvc/records/{0}")]
    [InlineData("/mvc/records?id={0}")]
    public async Task A_token_binds_to_the_identifier_it_encodes(string template)
    {
        var token = PublicId<Records>.From(42).ToToken();

        var body = await _host.Client.GetStringAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, token));

        Assert.Equal("42", body);
    }

    [Theory]
    [InlineData("/minimal/records/42")]
    [InlineData("/minimal/records?id=42")]
    [InlineData("/mvc/records?id=42")]
    [InlineData("/minimal/records/m1_k1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("/mvc/records/m1_k1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_decimal_or_inauthentic_value_is_refused_400(string path)
    {
        var response = await _host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("AAAAAAAA", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_decimal_route_segment_never_matches_the_constrained_mvc_route()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _host.Client.GetAsync("/mvc/records/42")).StatusCode);
    }

    [Fact]
    public async Task A_token_of_another_family_is_refused()
    {
        var userToken = PublicId<Users>.From(42).ToToken();

        Assert.Equal(HttpStatusCode.BadRequest, (await _host.Client.GetAsync($"/minimal/records/{userToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.Client.GetAsync($"/mvc/records/{userToken}")).StatusCode);
    }

    [Fact]
    public async Task Mvc_refuses_a_repeated_query_key_and_binds_null_on_a_nullable_parameter()
    {
        var token = PublicId<Records>.From(1).ToToken();

        Assert.Equal(HttpStatusCode.BadRequest, (await _host.Client.GetAsync($"/mvc/records?id={token}&id={token}")).StatusCode);
        Assert.Equal("none", await _host.Client.GetStringAsync("/mvc/optional"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.Client.GetAsync("/mvc/records")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_documents_every_identifier_as_an_opaque_patterned_string()
    {
        var document = await _host.Client.GetStringAsync("/openapi/v1.json");

        Assert.Contains(PublicIdFormat.OpenApiPattern, document, StringComparison.Ordinal);
        Assert.Contains("opaque identifier", document, StringComparison.Ordinal);
    }

    [Fact]
    public void The_constraint_and_the_binder_provider_are_registered_once_and_first()
    {
        var route = _host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RouteOptions>>().Value;
        var mvc = _host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MvcOptions>>().Value;

        Assert.Equal(typeof(PublicIdRouteConstraint), route.ConstraintMap[PublicIdRouteConstraint.ConstraintName]);
        Assert.IsType<PublicIdModelBinderProvider>(mvc.ModelBinderProviders[0]);
        Assert.Single(mvc.ModelBinderProviders, provider => provider is PublicIdModelBinderProvider);
    }

    private static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/minimal/records/{id}", (PublicId<Records> id) => id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        endpoints.MapGet("/minimal/records", (PublicId<Records> id) => id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        endpoints.MapPost("/minimal/records", (RecordInput input) => input.Id.Value);
        endpoints.MapOpenApi();
    }
}

/// <summary>A body carrying an identifier (OpenAPI schema check).</summary>
public sealed record RecordInput(PublicId<Records> Id);

/// <summary>The MVC side of the binding tests.</summary>
[ApiController]
public sealed class RecordsController : ControllerBase
{
    [HttpGet("/mvc/records/{id:publicid}")]
    public ActionResult<string> ByRoute(PublicId<Records> id) => Ok(id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [HttpGet("/mvc/records")]
    public ActionResult<string> ByQuery([FromQuery] PublicId<Records> id) => Ok(id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [HttpGet("/mvc/optional")]
    public ActionResult<string> Optional([FromQuery] PublicId<Records>? id) => Ok(id?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none");
}
