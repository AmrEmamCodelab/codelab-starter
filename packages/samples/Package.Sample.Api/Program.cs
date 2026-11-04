using Codelab.Errors;
using Codelab.Observability;
using Codelab.Security;
using Codelab.Security.Signing;
using Package.Sample.Api.Archive;
using Package.Sample.Api.Authentication;
using Package.Sample.Api.Hosting;
using Package.Sample.Api.Records;

var builder = WebApplication.CreateBuilder(args);

builder.AddCodelabObservability(SampleNames.ServiceName);         // Serilog, correlation id, PII-safe sinks
builder.Services.AddCodelabErrors();                               // one problem+json envelope
builder.Services.Configure<RouteHandlerOptions>(routes => routes.ThrowOnBadRequest = true); // binding failures get it too
builder.AddCodelabSecurity();                                      // key guards (fail fast), PublicIds, signing, sessions
builder.Services.AddSampleAuthentication(builder.Configuration);   // bearer sessions + the ES256 service scheme
builder.Services.AddArchiveClient(builder.Configuration);          // Codelab.Integration outbound client
builder.Services.AddSingleton<RecordStore>();

var app = builder.Build();

app.UseCodelabObservability();      // first: request line and X-Correlation-ID
app.UseCodelabErrors();             // then the error contract
app.UseRouting();
app.UseAuthentication();
app.UseCodelabUserLogContext();     // UserIdHash on every line
app.UseCodelabSecurity();           // after authentication: rate limit, sid revocation, request and response signing
app.UseAuthorization();

app.MapCodelabHealth();
app.MapCodelabSigningKeyEndpoint();
app.MapRecordEndpoints();

app.Run();

/// <summary>The sample host's entry point (public so the HTTP tests can start it).</summary>
public partial class Program;
