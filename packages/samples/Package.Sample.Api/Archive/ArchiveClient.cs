using Codelab.Errors;
using Codelab.Integration.Outbound;
using Codelab.Observability;
using Codelab.Security.PublicIds;
using Package.Sample.Api.Records;

namespace Package.Sample.Api.Archive;

/// <summary>What the archive upstream receives: the record's opaque id, never the database id.</summary>
public sealed record ArchiveRequest(string RecordToken);

/// <summary>
/// A typed outbound client built by Codelab.Integration: start-up-validated options (section
/// <see cref="SectionName"/>), retry and timeout policy, and server certificate validation that is always on.
/// It wraps only its outbound call and rethrows any failure as <see cref="UpstreamUnavailableException"/> (502).
/// </summary>
public sealed class ArchiveClient(HttpClient http)
{
    /// <summary>The client name (options name and the upstream named in a 502).</summary>
    public const string Name = "archive";

    /// <summary>The configuration section of the client (base address, retry, timeout, certificate trust).</summary>
    public const string SectionName = "Sample:Archive";

    /// <summary>The upstream path, relative to the configured base address.</summary>
    public const string ArchivePath = "archive/records";

    /// <summary>Sends one record to the archive.</summary>
    public async Task ArchiveAsync(PublicId<RecordIds> id, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(ArchivePath, new ArchiveRequest(id.ToToken()), cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new UpstreamUnavailableException(Name, exception);
        }
    }
}

/// <summary>Registration of the archive client.</summary>
public static class ArchiveClientRegistration
{
    /// <summary>
    /// Adds <see cref="ArchiveClient"/> through Codelab.Integration. The archive is an external system, so no trace or
    /// correlation header leaves the host (<c>AsExternalService</c>).
    /// </summary>
    public static IServiceCollection AddArchiveClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddCodelabOutboundClient<ArchiveClient>(ArchiveClient.Name, configuration.GetSection(ArchiveClient.SectionName))
            .AsExternalService();
        return services;
    }
}
