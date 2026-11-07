using Codelab.Errors;
using Codelab.Gateway.Api.Hosting;
using Codelab.Observability;
using Codelab.Security;

var builder = WebApplication.CreateBuilder(args);
builder.AddLocalSecretsFile();                                                         // Development: appsettings.Development.local.json
builder.AddEphemeralPublicIdRing();                                                    // the gateway can never read an API identifier
builder.AddCodelabObservability(GatewayHostSettings.ServiceNameFrom(builder.Configuration));
builder.Services.AddCodelabErrors();                                                   // one problem+json envelope
builder.AddCodelabSecurity();                                                          // protocol, replay guard, headers
builder.Services.AddGatewayHost(builder.Configuration);                                // S2S scheme, profiles, upstream clients, readiness

var app = builder.Build();

app.UseCodelabObservability();           // first: request line and X-Correlation-ID
app.UseCodelabErrors();                  // then the error contract
app.UseGatewayPipeline();                // routing, authentication, authorization
app.MapCodelabHealth();                  // /health/live, /health/ready (upstream configuration)
app.MapGatewayRelays();                  // /relay/{profile}/... behind the service assertion

app.Run();

/// <summary>The gateway's entry point (public so the HTTP tests can start it).</summary>
public partial class Program;
