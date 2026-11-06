using Codelab.Attachments.Api.Hosting;
using Codelab.Attachments.Core;
using Codelab.Errors;
using Codelab.Observability;
using Codelab.Security;

var builder = WebApplication.CreateBuilder(args);
builder.AddLocalSecretsFile();                                                          // Development: appsettings.Development.local.json
builder.AddEphemeralPublicIdRing();                                                     // the service can never read an API identifier
builder.AddCodelabObservability(AttachmentsHostSettings.ServiceNameFrom(builder.Configuration));
builder.Services.AddCodelabErrors();                                                    // one problem+json envelope
builder.AddCodelabSecurity();                                                           // protocol, tickets, receipts, replay guard, headers
builder.Services.AddAttachmentsCore(builder.Configuration);                             // file-share store + upload pipeline
builder.Services.AddAttachmentsHost(builder.Configuration);                             // S2S + ticket schemes, policies, upload CORS

var app = builder.Build();

app.UseCodelabObservability();           // first: request line and X-Correlation-ID
app.UseCodelabErrors();                  // then the error contract
app.UseAttachmentsPipeline();            // routing, upload CORS, authentication, authorization
app.MapCodelabHealth();                  // /health/live, /health/ready (storage root)
app.MapAttachmentsEndpoints();           // POST /api/uploads, GET and DELETE /api/files/{fileId}

app.Run();

/// <summary>The service's entry point (public so the HTTP tests can start it).</summary>
public partial class Program;
