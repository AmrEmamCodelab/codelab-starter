using Microsoft.AspNetCore.Builder;

namespace Codelab.Security.Signing;

/// <summary>How much of a request a <see cref="SignatureExemptAttribute"/> takes out of signature coverage.</summary>
public enum SignatureExemption
{
    /// <summary>The narrowest: the proof is still required, but the body is covered as <c>UNSIGNED-BODY</c> (multipart uploads).</summary>
    BodyOnly = 0,

    /// <summary>No proof at all (anonymous endpoints such as login).</summary>
    WholeRequest = 1,

    /// <summary>The signing-key issuance endpoint: the caller has no key yet.</summary>
    KeyIssuance = 2,
}

/// <summary>Takes an endpoint out of request-signature coverage. A reason is mandatory. Not an authorization attribute.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SignatureExemptAttribute : Attribute
{
    /// <summary>Creates the exemption.</summary>
    /// <exception cref="ArgumentException">The reason is blank.</exception>
    public SignatureExemptAttribute(SignatureExemption scope, string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        Scope = scope;
        Why = why;
    }

    /// <summary>The exempted scope.</summary>
    public SignatureExemption Scope { get; }

    /// <summary>Why the endpoint is exempt (reviewed).</summary>
    public string Why { get; }
}

/// <summary>
/// The response body is not hashed (file downloads): the response signature still covers the status, the content type
/// and the request binding, with the body field set to <c>UNSIGNED-BODY</c>. A reason is mandatory.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class UnsignedResponseAttribute : Attribute
{
    /// <summary>Creates the marker.</summary>
    /// <exception cref="ArgumentException">The reason is blank.</exception>
    public UnsignedResponseAttribute(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        Why = why;
    }

    /// <summary>Why the body is not covered.</summary>
    public string Why { get; }
}

/// <summary>Minimal-API equivalents of the signature markers.</summary>
public static class SignatureEndpointConventions
{
    /// <summary>Adds a <see cref="SignatureExemptAttribute"/> to the endpoint.</summary>
    public static TBuilder WithSignatureExemption<TBuilder>(this TBuilder builder, SignatureExemption scope, string why)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SignatureExemptAttribute(scope, why));

    /// <summary>Adds an <see cref="UnsignedResponseAttribute"/> to the endpoint.</summary>
    public static TBuilder WithUnsignedResponse<TBuilder>(this TBuilder builder, string why)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new UnsignedResponseAttribute(why));
}

/// <summary>
/// What a verified request leaves for the response signer: the session key and the request nonce. Lives in
/// <c>HttpContext.Items</c> under a private key; <see cref="ToString"/> never prints the key.
/// </summary>
public sealed class SignedRequestBinding
{
    private static readonly object ItemKey = new();

    /// <summary>Creates the binding.</summary>
    public SignedRequestBinding(byte[] sessionKey, string requestNonce)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestNonce);
        SessionKey = sessionKey;
        RequestNonce = requestNonce;
    }

    /// <summary>The derived session key.</summary>
    public byte[] SessionKey { get; }

    /// <summary>The verified request's nonce.</summary>
    public string RequestNonce { get; }

    /// <summary>Stores the binding on the request.</summary>
    public static void Stash(HttpContext context, SignedRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(binding);
        context.Items[ItemKey] = binding;
    }

    /// <summary>The binding of a verified request, or null.</summary>
    public static SignedRequestBinding? Find(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ItemKey, out var stored) ? stored as SignedRequestBinding : null;
    }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(SignedRequestBinding)} {{ SessionKey = [redacted], RequestNonce = {RequestNonce} }}";
}
