using Codelab.Security.PublicIds;

namespace Starter.Modules.Jobs.Application;

/// <summary>The family of job run ids on the wire (<c>PublicId&lt;JobRunIds&gt;</c>).</summary>
public sealed class JobRunIds : IIdDomain
{
    /// <summary>The domain label bound into every job run PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "job-runs";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
