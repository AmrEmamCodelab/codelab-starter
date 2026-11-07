using Codelab.Errors;
using Codelab.Observability;
using Codelab.OpsConsole.Api.Hosting;
using Codelab.Security;

var builder = WebApplication.CreateBuilder(args);
builder.AddLocalSecretsFile();                                                          // Development: appsettings.Development.local.json
builder.AddEphemeralPublicIdRing();                                                     // the console never reads an API identifier
builder.AddCodelabObservability(OpsConsoleHostSettings.ServiceNameFrom(builder.Configuration));
builder.Services.AddCodelabErrors();                                                    // one problem+json envelope
builder.AddCodelabSecurity();                                                           // protocol, clock, headers, replay guard
builder.AddOpsConsoleHost();                                                            // operators, /api/ops client, Seq reader

var app = builder.Build();

app.UseCodelabObservability();           // first: request line and X-Correlation-ID
app.UseCodelabErrors();                  // then the error contract
app.UseOpsConsolePipeline();             // routing, Windows sign-in, operator group
app.MapCodelabHealth();                  // /health/live, /health/ready
app.MapOpsConsoleEndpoints();            // GET /ops-api/{me,health,jobs,certificates,reference-codes/{code}}

app.Run();

/// <summary>The console's entry point (public so the HTTP tests can start it).</summary>
public partial class Program;
