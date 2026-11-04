namespace Codelab.Security.Signing;

/// <summary>
/// The rollout state of an enforcing control (request proofs, response signatures). Every control moves
/// Off, then Report, then Enforce, and never skips Report: a clean Report window against real traffic is the evidence
/// that Enforce will not turn a canonicalisation disagreement into an outage. Stored and configured as an integer or
/// its name; the values are part of the configuration contract.
/// </summary>
public enum SecurityEnforcementMode
{
    /// <summary>The control does not run: no header read, no body touched, no service resolved.</summary>
    Off = 0,

    /// <summary>The control runs and logs its verdict; a failure never changes the response.</summary>
    Report = 1,

    /// <summary>The control runs and a failure rejects the request.</summary>
    Enforce = 2,
}
