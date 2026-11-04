using Codelab.Security.Keys;
using Microsoft.Extensions.Options;

namespace Codelab.Security.ServiceAuth;

/// <summary>Validates the client options at start: required fields, a P-256 private key, not published material.</summary>
internal sealed class ServiceAssertionClientOptionsValidator(IServiceProvider services) : IValidateOptions<ServiceAssertionClientOptions>
{
    public ValidateOptionsResult Validate(string? name, ServiceAssertionClientOptions options)
    {
        const string section = ServiceAssertionClientOptions.SectionName;
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ServiceName) || string.IsNullOrWhiteSpace(options.Audience) || string.IsNullOrWhiteSpace(options.KeyId))
        {
            problems.Add($"{section}: ServiceName, Audience and KeyId are required.");
        }

        if (options.LifetimeSeconds <= 0)
        {
            problems.Add($"{section}:LifetimeSeconds must be positive.");
        }

        if (ServiceKeys.Problem(options.PrivateKeyPem, $"{section}:PrivateKeyPem") is { } keyProblem)
        {
            problems.Add(keyProblem);
        }

        var published = KeyRingGuardFactory.Create(services).CheckNotPublished([new KeyEntryOptions { KeyId = options.KeyId, MaterialBase64 = options.PrivateKeyPem }], section);
        if (!published.IsValid)
        {
            problems.Add(published.Error!);
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>
/// Validates the server options at start: audience, positive limits, unique key ids, P-256 public keys, and no trusted
/// public key that is published material. A published public key (a starter test fixture) has its private half in the
/// repository, so trusting it lets anyone mint assertions the server accepts.
/// </summary>
internal sealed class ServiceAssertionServerOptionsValidator(IServiceProvider services) : IValidateOptions<ServiceAssertionServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServiceAssertionServerOptions options)
    {
        const string section = ServiceAssertionServerOptions.SectionName;
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Audience) || options.TrustedServices.Count == 0)
        {
            problems.Add($"{section}: Audience and at least one TrustedServices entry are required.");
        }

        if (options.MaxLifetimeSeconds <= 0 || options.ClockSkewSeconds < 0 || options.MaxBodyBytes <= 0)
        {
            problems.Add($"{section}: MaxLifetimeSeconds and MaxBodyBytes must be positive, ClockSkewSeconds not negative.");
        }

        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in options.TrustedServices)
        {
            problems.AddRange(TrustedServiceProblems(service, keyIds, section));
        }

        var trustedKeys = options.TrustedServices.Select(service => new KeyEntryOptions { KeyId = service.KeyId, MaterialBase64 = service.PublicKeyPem });
        var published = KeyRingGuardFactory.Create(services).CheckNotPublished(trustedKeys, $"{section}:TrustedServices");
        if (!published.IsValid)
        {
            problems.Add(published.Error!);
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private static IEnumerable<string> TrustedServiceProblems(TrustedServiceOptions service, HashSet<string> keyIds, string section)
    {
        if (string.IsNullOrWhiteSpace(service.ServiceName) || string.IsNullOrWhiteSpace(service.KeyId))
        {
            yield return $"{section}:TrustedServices entries need ServiceName and KeyId.";
            yield break;
        }

        if (!keyIds.Add(service.KeyId))
        {
            yield return $"{section}:TrustedServices has the KeyId '{service.KeyId}' twice.";
        }

        if (ServiceKeys.Problem(service.PublicKeyPem, $"{section}:TrustedServices[{service.KeyId}]:PublicKeyPem") is { } keyProblem)
        {
            yield return keyProblem;
        }
    }
}
