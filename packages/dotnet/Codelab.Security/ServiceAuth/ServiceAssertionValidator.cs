using System.Security.Cryptography;
using Codelab.Security.Canonical;
using Codelab.Security.Replay;

namespace Codelab.Security.ServiceAuth;

/// <summary>Why an assertion was accepted or refused.</summary>
public enum ServiceAssertionOutcome
{
    /// <summary>Accepted (and its jti consumed).</summary>
    Valid = 0,

    /// <summary>No assertion header.</summary>
    Missing = 1,

    /// <summary>Not a well-formed ES256 assertion of this protocol.</summary>
    Malformed = 2,

    /// <summary>The key id is not trusted, or the issuer is not the key's service.</summary>
    UnknownKey = 3,

    /// <summary>The signature does not verify.</summary>
    BadSignature = 4,

    /// <summary>The audience is not this service.</summary>
    WrongAudience = 5,

    /// <summary>Expired, issued in the future, or a lifetime above the maximum.</summary>
    Expired = 6,

    /// <summary>Method, target or body hash do not match this request.</summary>
    BindingMismatch = 7,

    /// <summary>The jti was already used.</summary>
    Replayed = 8,

    /// <summary>The replay store could not answer (fail closed).</summary>
    StoreUnavailable = 9,
}

/// <summary>The request an assertion must match.</summary>
public sealed record ServiceAssertionRequest(string? Token, string Method, string Target, string BodyHash);

/// <summary>The verdict; <see cref="ServiceName"/> is set when valid.</summary>
public sealed record ServiceAssertionResult(ServiceAssertionOutcome Outcome, string? ServiceName)
{
    /// <summary>A refusal.</summary>
    public static ServiceAssertionResult Refused(ServiceAssertionOutcome outcome) => new(outcome, ServiceName: null);
}

/// <summary>
/// Validates service assertions: ES256 only, trusted key id, issuer bound to the key, audience, lifetime window,
/// method/target/body binding, then single use of the <c>jti</c> through the replay guard (scope <c>s2s/{issuer}</c>).
/// </summary>
public sealed class ServiceAssertionValidator : IDisposable
{
    private const string ReplayScopePrefix = "s2s/";

    private readonly ServiceAssertionServerOptions _options;
    private readonly IReplayGuard _replayGuard;
    private readonly TimeProvider _clock;
    private readonly SecurityProtocol _protocol;
    private readonly Dictionary<string, (string ServiceName, ECDsa Key)> _trusted;

    /// <summary>Creates the validator; throws when a trusted key does not import.</summary>
    public ServiceAssertionValidator(ServiceAssertionServerOptions options, IReplayGuard replayGuard, TimeProvider clock, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _replayGuard = replayGuard ?? throw new ArgumentNullException(nameof(replayGuard));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _trusted = options.TrustedServices.ToDictionary(
            service => service.KeyId!,
            service => (service.ServiceName!, ServiceKeys.ImportP256(service.PublicKeyPem!)),
            StringComparer.Ordinal);
    }

    /// <summary>Validates <paramref name="request"/>.</summary>
    public async Task<ServiceAssertionResult> ValidateAsync(ServiceAssertionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (outcome, parsed) = Check(request);
        if (outcome is not ServiceAssertionOutcome.Valid)
        {
            return ServiceAssertionResult.Refused(outcome);
        }

        var claims = parsed!.Claims;
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(claims.ExpiresAtUnix).AddSeconds(_options.ClockSkewSeconds);
        var consumed = await _replayGuard.TryConsumeAsync(new ReplayNonce(ReplayScopePrefix + claims.Issuer, claims.Id, expiresAt), cancellationToken);
        return consumed switch
        {
            NonceConsumeOutcome.Fresh => new ServiceAssertionResult(ServiceAssertionOutcome.Valid, claims.Issuer),
            NonceConsumeOutcome.Replayed => ServiceAssertionResult.Refused(ServiceAssertionOutcome.Replayed),
            _ => ServiceAssertionResult.Refused(ServiceAssertionOutcome.StoreUnavailable),
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (_, key) in _trusted.Values)
        {
            key.Dispose();
        }
    }

    private (ServiceAssertionOutcome Outcome, ParsedServiceAssertion? Parsed) Check(ServiceAssertionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return (ServiceAssertionOutcome.Missing, null);
        }

        if (!ServiceAssertionToken.TryRead(request.Token, out var parsed)
            || parsed.Header.Algorithm != ServiceAssertionToken.Algorithm
            || parsed.Header.Type != _protocol.ServiceAssertionType)
        {
            return (ServiceAssertionOutcome.Malformed, null);
        }

        if (!_trusted.TryGetValue(parsed.Header.KeyId, out var trusted) || trusted.ServiceName != parsed.Claims.Issuer)
        {
            return (ServiceAssertionOutcome.UnknownKey, null);
        }

        return (Verify(parsed, trusted.Key, request), parsed);
    }

    private ServiceAssertionOutcome Verify(ParsedServiceAssertion parsed, ECDsa key, ServiceAssertionRequest request)
    {
        if (!parsed.VerifySignature(key))
        {
            return ServiceAssertionOutcome.BadSignature;
        }

        if (parsed.Claims.Audience != _options.Audience)
        {
            return ServiceAssertionOutcome.WrongAudience;
        }

        if (!IsWithinWindow(parsed.Claims))
        {
            return ServiceAssertionOutcome.Expired;
        }

        return IsBound(parsed.Claims, request) ? ServiceAssertionOutcome.Valid : ServiceAssertionOutcome.BindingMismatch;
    }

    private bool IsWithinWindow(ServiceAssertionClaims claims)
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        var skew = _options.ClockSkewSeconds;
        var lifetime = claims.ExpiresAtUnix - claims.IssuedAtUnix;
        return claims.IssuedAtUnix <= now + skew && now <= claims.ExpiresAtUnix + skew && lifetime > 0 && lifetime <= _options.MaxLifetimeSeconds;
    }

    private static bool IsBound(ServiceAssertionClaims claims, ServiceAssertionRequest request) =>
        CanonicalText.TryCanonicalMethod(request.Method, out var method)
        && string.Equals(claims.Method, method, StringComparison.Ordinal)
        && string.Equals(claims.Target, request.Target, StringComparison.Ordinal)
        && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(claims.BodyHash), System.Text.Encoding.ASCII.GetBytes(request.BodyHash));
}
