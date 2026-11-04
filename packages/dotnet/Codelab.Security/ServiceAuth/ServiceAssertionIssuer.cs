using System.Security.Cryptography;
using Codelab.Security.Canonical;
using Codelab.Security.Signing;

namespace Codelab.Security.ServiceAuth;

/// <summary>Issues one assertion per outgoing request, bound to its method, target and body hash.</summary>
public sealed class ServiceAssertionIssuer : IDisposable
{
    private const int IdBytes = 16;

    private readonly ServiceAssertionClientOptions _options;
    private readonly TimeProvider _clock;
    private readonly SecurityProtocol _protocol;
    private readonly ECDsa _key;

    /// <summary>Creates the issuer.</summary>
    /// <exception cref="ArgumentException">A required option is missing.</exception>
    /// <exception cref="CryptographicException">The key is not a P-256 private key.</exception>
    public ServiceAssertionIssuer(ServiceAssertionClientOptions options, TimeProvider clock, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ServiceName, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Audience, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.KeyId, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PrivateKeyPem, nameof(options));
        _options = options;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _key = ServiceKeys.ImportP256(options.PrivateKeyPem);
    }

    /// <summary>The assertion for a request: method, path, query and the exact body bytes.</summary>
    public string Issue(HttpMethod method, Uri requestUri, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(requestUri);
        var now = _clock.GetUtcNow();
        var claims = new ServiceAssertionClaims(
            Issuer: _options.ServiceName!,
            Audience: _options.Audience!,
            IssuedAtUnix: now.ToUnixTimeSeconds(),
            ExpiresAtUnix: now.AddSeconds(_options.LifetimeSeconds).ToUnixTimeSeconds(),
            Id: Base64UrlText.Encode(RandomNumberGenerator.GetBytes(IdBytes)),
            Method: CanonicalText.CanonicalMethod(method.Method),
            Target: ServiceAssertionToken.Target(requestUri.AbsolutePath, requestUri.Query),
            BodyHash: SignatureCanonicalForm.HashBody(body));
        var header = new ServiceAssertionHeader(ServiceAssertionToken.Algorithm, _protocol.ServiceAssertionType, _options.KeyId!);
        return ServiceAssertionToken.Create(header, claims, _key);
    }

    /// <inheritdoc />
    public void Dispose() => _key.Dispose();
}

/// <summary>The outgoing handler: buffers the request body, issues an assertion and adds <c>X-{Prefix}-Service-Assertion</c>.</summary>
public sealed class ServiceAssertionHandler(ServiceAssertionIssuer issuer, SecurityProtocol protocol) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri ?? throw new InvalidOperationException("A service call needs an absolute request URI.");
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        request.Headers.Remove(protocol.ServiceAssertionHeaderName);
        request.Headers.TryAddWithoutValidation(protocol.ServiceAssertionHeaderName, issuer.Issue(request.Method, uri, body));
        return await base.SendAsync(request, cancellationToken);
    }
}
