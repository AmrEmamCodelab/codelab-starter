using System.Net;
using Codelab.Testing.Tests.Fixtures.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codelab.Testing.Tests.Support;

/// <summary>A small host with minimal-API and controller endpoints for the contract checks.</summary>
internal static class EndpointFixtureHost
{
    public static WebApplication Build(Action<WebApplication> map, bool withControllers = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port: 0));
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails();
        if (withControllers)
        {
            builder.Services.AddControllers().AddApplicationPart(typeof(FixtureOrdersController).Assembly);
        }

        var app = builder.Build();
        map(app);
        if (withControllers)
        {
            app.MapControllers();
        }

        return app;
    }

    public static IReadOnlyList<Endpoint> EndpointsOf(WebApplication app) =>
        [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)];

    public static async Task<HttpClient> StartAsync(WebApplication app)
    {
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    /// <summary>The endpoints of the permission and validator checks.</summary>
    public static void MapRecordEndpoints(WebApplication app)
    {
        app.MapGet("/records/{id}", (string id) => id).WithMetadata(new FixturePermissionAttribute(FixturePermissions.RecordsView));
        app.MapPost("/records", (RecordInput input) => input.Title).WithMetadata(new FixturePermissionAttribute(FixturePermissions.RecordsView));
        app.MapPost("/notes", (NoteInput input) => input.Text).WithMetadata(new FixturePermissionAttribute(FixturePermissions.RecordsView));
        app.MapGet("/health", () => "ok").AllowAnonymous();
        app.MapGet("/open", () => "open").AllowAnonymous();
        app.MapGet("/bare", () => "bare").RequireAuthorization();
    }
}
