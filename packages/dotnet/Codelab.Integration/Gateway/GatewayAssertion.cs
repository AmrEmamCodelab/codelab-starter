using System.Security.Cryptography;
using Codelab.Security;
using Codelab.Security.ServiceAuth;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Gateway;

/// <summary>
/// The service-assertion issuer of the gateway client. It signs with this service's S2S identity
/// (<c>Codelab:Security:ServiceAssertions:Client</c>: ServiceName, KeyId, PrivateKeyPem, LifetimeSeconds) but with the
/// gateway's audience (<see cref="GatewayOptions.Audience"/>), so a host that also calls another service (with its own
/// audience in that section) keeps one key and two audiences.
/// </summary>
internal sealed class GatewayAssertionIssuer(IOptionsMonitor<ServiceAssertionClientOptions> options, TimeProvider clock, SecurityProtocol protocol) : IDisposable
{
    private readonly Lazy<ServiceAssertionIssuer> _issuer = new(() => new ServiceAssertionIssuer(options.Get(GatewayRelayClient.Name), clock, protocol));

    public ServiceAssertionIssuer Issuer => _issuer.Value;

    public void Dispose()
    {
        if (_issuer.IsValueCreated)
        {
            _issuer.Value.Dispose();
        }
    }
}

/// <summary>
/// Validates the gateway client's named assertion options at start: identity fields, a positive lifetime and a P-256
/// private key. (When the host also registers Codelab.Security's client scheme, its validator runs on these options
/// too and adds the published-key guard.)
/// </summary>
internal sealed class GatewayAssertionOptionsValidator : IValidateOptions<ServiceAssertionClientOptions>
{
    public ValidateOptionsResult Validate(string? name, ServiceAssertionClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(name, GatewayRelayClient.Name, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }

        const string section = ServiceAssertionClientOptions.SectionName;
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ServiceName) || string.IsNullOrWhiteSpace(options.KeyId) || string.IsNullOrWhiteSpace(options.Audience))
        {
            problems.Add($"{section}: ServiceName and KeyId (and {GatewayOptions.SectionName}:Audience) are required for the gateway client.");
        }

        if (options.LifetimeSeconds <= 0)
        {
            problems.Add($"{section}:LifetimeSeconds must be positive.");
        }

        if (!IsP256PrivateKey(options.PrivateKeyPem))
        {
            problems.Add($"{section}:PrivateKeyPem must be a P-256 (ES256) private key in PEM form.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private static bool IsP256PrivateKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return false;
        }

        using var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);
            return key.KeySize == ServiceAssertionToken.KeySizeBits && HasPrivateKey(key);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static bool HasPrivateKey(ECDsa key)
    {
        try
        {
            key.ExportParameters(includePrivateParameters: true);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
