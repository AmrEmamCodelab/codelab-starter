using Codelab.Errors;
using Codelab.Observability;
using Codelab.Security;
using Starter.Api.Hosting;
using Starter.Infrastructure.Configuration;
using Starter.Infrastructure.Errors;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Modularity;

var hostAssembly = typeof(Program).Assembly;
var modules = ModuleCatalog.Discover(hostAssembly);   // from the module ProjectReferences of Starter.Api.csproj

var builder = WebApplication.CreateBuilder(args);
builder.AddLocalSecretsFile();                                               // Development: appsettings.Development.local.json
builder.AddCodelabObservability(ModuleNaming.ServiceNameOf(hostAssembly));   // Serilog, correlation id, PII-safe sinks
builder.Services.AddStarterKernel(builder.Configuration);                    // before security: migrations run first
builder.Services.AddCodelabErrors(errors => errors.AddStarterFailures());    // one problem+json envelope
builder.AddCodelabSecurity();                                                // keys, PublicIds, signing, sessions, CORS
builder.Services.AddStarterModules(modules, builder.Configuration);          // every module, plus MVC controllers
builder.Services.AddStarterOpenApi();                                        // document v1 with PublicId schemas

var app = builder.Build();

app.UseCodelabObservability();           // first: request line and X-Correlation-ID
app.UseCodelabErrors();                  // then the error contract
app.UseStarterPipeline();                // CORS, routing, authentication, security, authorization
app.MapStarterEndpoints(modules);        // controllers, module extras, signing key, health
app.MapStarterOpenApi();                 // /openapi/v1.json in Development

app.Run();

/// <summary>The host's entry point (public so the HTTP tests and DocsGen can start it).</summary>
public partial class Program;
