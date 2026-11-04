using Codelab.Testing.Checks;

namespace Codelab.Testing.Vectors;

/// <summary>What the compute delegate gets besides the case.</summary>
/// <param name="Prefix">The protocol prefix of this run; the delegate must build canonical strings and signatures with it.</param>
/// <param name="PinnedPrefix">The prefix the file's expected values were generated with.</param>
/// <param name="FixtureKeyId">The fixture key the file's expected values were generated with.</param>
public sealed record VectorRunContext(string Prefix, string PinnedPrefix, string FixtureKeyId)
{
    /// <summary>True when the run uses the prefix the vectors were generated with, so every expected value applies.</summary>
    public bool IsPinnedPrefix => string.Equals(Prefix, PinnedPrefix, StringComparison.Ordinal);
}

/// <summary>
/// Runs golden vectors against a canonicalizer or signer supplied as a delegate, so this package needs no reference
/// to the security package. The protocol prefix is a parameter: with the pinned prefix every expected value is
/// compared; with another prefix (a renamed fork) the <see cref="VectorExpectationRebaser"/> decides which expected
/// values can be rebased and which are skipped.
/// </summary>
public sealed class GoldenVectorRunner
{
    private readonly string _prefix;
    private readonly VectorExpectationRebaser _rebaser;

    /// <summary>Creates a runner for <paramref name="prefix"/>; <paramref name="rebaser"/> defaults to <see cref="VectorExpectationRebaser.Default"/>.</summary>
    public GoldenVectorRunner(string prefix, VectorExpectationRebaser? rebaser = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _prefix = prefix;
        _rebaser = rebaser ?? VectorExpectationRebaser.Default;
    }

    /// <summary>
    /// Runs every case: <paramref name="compute"/> returns the produced values keyed by the expected field name
    /// (<c>expectedCanonicalBase64</c> → value). A missing produced value is a mismatch.
    /// </summary>
    public GoldenVectorReport Run(GoldenVectorFile file, Func<GoldenVectorCase, VectorRunContext, IReadOnlyDictionary<string, string>> compute)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(compute);
        var context = new VectorRunContext(_prefix, file.Prefix, file.FixtureKeyId);
        var report = new GoldenVectorReport(context);
        foreach (var vectorCase in file.Cases)
        {
            RunCase(vectorCase, context, compute(vectorCase, context), report);
        }

        return report;
    }

    private void RunCase(GoldenVectorCase vectorCase, VectorRunContext context, IReadOnlyDictionary<string, string> produced, GoldenVectorReport report)
    {
        foreach (var (field, pinned) in vectorCase.Expected)
        {
            var expected = context.IsPinnedPrefix ? pinned : _rebaser.Rebase(field, pinned, context);
            if (expected is null)
            {
                report.Skip();
                continue;
            }

            report.Compare(vectorCase.Name, field, expected, produced.GetValueOrDefault(field));
        }
    }
}

/// <summary>The outcome of a vector run.</summary>
public sealed class GoldenVectorReport
{
    private readonly List<string> _mismatches = [];

    internal GoldenVectorReport(VectorRunContext context) => Context = context;

    /// <summary>The run's prefixes and fixture key.</summary>
    public VectorRunContext Context { get; }

    /// <summary>How many expected values were compared.</summary>
    public int Compared { get; private set; }

    /// <summary>How many expected values could not apply to this prefix and were skipped.</summary>
    public int Skipped { get; private set; }

    /// <summary>Every mismatch, one line each.</summary>
    public IReadOnlyList<string> Mismatches => _mismatches;

    /// <summary>The report as a check: mismatches are violations; comparing nothing fails as vacuous.</summary>
    public CheckResult ToCheckResult() =>
        new($"Golden vectors (prefix {Context.Prefix}, pinned {Context.PinnedPrefix}, {Skipped} skipped)", _mismatches, Compared);

    /// <summary>Throws <see cref="ContractViolationException"/> unless every compared value matched and something was compared.</summary>
    public void ThrowIfFailed() => ToCheckResult().ThrowIfFailed();

    internal void Skip() => Skipped++;

    internal void Compare(string caseName, string field, string expected, string? actual)
    {
        Compared++;
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            _mismatches.Add($"{caseName}.{field}: expected '{expected}', got '{actual ?? "<not produced>"}'");
        }
    }
}
