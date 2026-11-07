namespace Starter.Application.SingleSignOn;

/// <summary>
/// The identity provider's JSON Web Key Set as this host's single sign-on reads it: over the same back channel as the
/// sign-in (direct, or the integration gateway relay in a backend zone without egress), so a reader never needs a
/// route of its own to the provider. Implemented by the Identity module while single sign-on is a login mode; absent
/// otherwise, so a consumer (the Ops certificate list) takes it as optional.
/// </summary>
public interface IIdentityProviderKeySet
{
    /// <summary>
    /// The JWKS document as the provider sent it (JSON text), or null when the provider or the relay could not answer
    /// with a success. Never throws for a provider or transport fault; a cancellation of
    /// <paramref name="cancellationToken"/> propagates.
    /// </summary>
    Task<string?> ReadJwksAsync(CancellationToken cancellationToken);
}
