namespace Starter.Modules.ServiceRequests.Tests.Http;

/// <summary>
/// The HTTP tests run one host at a time, never in parallel with anything else: each host installs its own PublicId
/// keys in the process-wide codec accessor, so two hosts at once would read each other's tokens.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HttpTestGroup
{
    /// <summary>The collection name.</summary>
    public const string Name = "service-requests-http";
}
