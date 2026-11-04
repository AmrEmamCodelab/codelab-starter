using Codelab.Security.Replay;

namespace Codelab.Security.Signing;

/// <summary>The services the signing middleware needs, resolved per request so a broken key ring degrades, not crashes.</summary>
internal sealed record SigningServices(
    SecurityProtocol Protocol,
    IProofVerifier Verifier,
    IReplayGuard ReplayGuard,
    ISigningKeyring Keyring,
    SessionKeyDerivation Derivation)
{
    internal static SigningServices? TryResolve(IServiceProvider services, out Exception? failure)
    {
        failure = null;
        try
        {
            return new SigningServices(
                services.GetRequiredService<SecurityProtocol>(),
                services.GetRequiredService<IProofVerifier>(),
                services.GetRequiredService<IReplayGuard>(),
                services.GetRequiredService<ISigningKeyring>(),
                services.GetRequiredService<SessionKeyDerivation>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex;
            return null;
        }
    }
}
