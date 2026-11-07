namespace Starter.Modules.Ops.Domain;

/// <summary>The Ops endpoint an operator called (stored as the integer value).</summary>
public enum OpsEndpoint
{
    /// <summary><c>GET api/ops/health</c>.</summary>
    Health = 1,

    /// <summary><c>GET api/ops/jobs</c>.</summary>
    Jobs = 2,

    /// <summary><c>GET api/ops/certificates</c>.</summary>
    Certificates = 3,

    /// <summary><c>GET api/ops/reference-codes/(code)</c>: one reference code.</summary>
    ReferenceCode = 4,
}
