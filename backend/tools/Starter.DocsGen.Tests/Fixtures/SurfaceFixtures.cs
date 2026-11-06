using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Tests.Fixtures;

/// <summary>A small, hand-built surface for the renderer and command tests.</summary>
internal static class SurfaceFixtures
{
    public const string OpenApi = """
        {
          "openapi": "3.1.1",
          "servers": [ { "url": "http://localhost" } ],
          "paths": {
            "/api/zeta": { "get": { "summary": "Zeta" } },
            "/api/alpha": { "post": { "summary": "Alpha" } }
          },
          "components": { "schemas": { "Zed": { "type": "object" }, "Abc": { "type": "string" } } }
        }
        """;

    public static ApiSurface Surface() => new(
        OpenApiJson: OpenApi,
        Permissions:
        [
            new PermissionDoc("sample.view", "Sample", "Open the sample."),
            new PermissionDoc("other.edit", "Other", "Edit | other."),
        ],
        Endpoints:
        [
            new EndpointDoc("GET", "/api/sample", EndpointAccessKind.Permission, ["sample.view"], "Sample", "Reads the sample."),
            new EndpointDoc("POST", "/api/auth/login", EndpointAccessKind.Anonymous, [], "Identity", "Signs in."),
            new EndpointDoc("GET", "/api/me", EndpointAccessKind.Authenticated, [], "Access", "Me."),
        ],
        ErrorCodes:
        [
            new ErrorCodeDoc("SAMPLE_GONE", "SampleErrorCodes.Gone", "Sample", "The sample is gone."),
            new ErrorCodeDoc("A_FIRST", "AErrorCodes.First", "Alpha", "First."),
        ],
        ConfigKeys:
        [
            new ConfigKeyDoc("Sample:Limit", "int", "Sample", "Sample", "5", "The limit."),
            new ConfigKeyDoc("Sample:Name", "string", "Sample", "Sample", string.Empty, "The name."),
            new ConfigKeyDoc("Database:Apply", "bool", "Kernel", "Database", "false", "Apply."),
        ],
        Statuses:
        [
            new StatusDoc("RunStatus", "Sample.Domain.RunStatus", "Sample", "A run's | state.",
            [
                new StatusMemberDoc(3, "Done", "Finished."),
                new StatusMemberDoc(1, "Queued", "Waiting."),
            ]),
            new StatusDoc("EmptyStatus", "Alpha.EmptyStatus", "Alpha", string.Empty, []),
        ],
        Workflows: [FixtureRecordWorkflow.Doc()]);

    public static ApiSurface Empty() => new(OpenApi, [], [], [], [], [], []);
}

/// <summary>An <see cref="IApiSurfaceSource"/> that returns a fixed surface or throws.</summary>
internal sealed class FixedSurfaceSource(ApiSurface? surface, Exception? failure = null) : IApiSurfaceSource
{
    public int Calls { get; private set; }

    public Task<ApiSurface> CollectAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return failure is null ? Task.FromResult(surface!) : Task.FromException<ApiSurface>(failure);
    }
}

/// <summary>A temp folder that the test creates and removes (only its own folder).</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "starter-docsgen-tests-" + Guid.NewGuid().ToString("N"));
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
