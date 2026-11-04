using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codelab.Testing.Checks;
using Codelab.Testing.Vectors;

namespace Codelab.Testing.Tests.Vectors;

/// <summary>
/// The runner against a stand-in canonicalizer and signer (HMAC-SHA256 with a key generated per test run). The vector
/// file is produced by the same stand-in with the pinned prefix, exactly as the real generator pins CODELABTEST.
/// </summary>
public sealed class GoldenVectorRunnerTests
{
    private const string PinnedPrefix = "CODELABTEST";
    private const string OtherPrefix = "ALTPFX";
    private const string CanonicalField = "expectedCanonicalBase64";
    private const string SignatureField = "expectedSignatureBase64Url";
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void The_pinned_prefix_compares_every_expected_value()
    {
        var report = new GoldenVectorRunner(PinnedPrefix).Run(PinnedFile(), Compute);

        Assert.Equal(4, report.Compared);
        Assert.Equal(0, report.Skipped);
        report.ThrowIfFailed();
    }

    [Fact]
    public void Another_prefix_rebases_canonical_values_and_skips_signatures()
    {
        var report = new GoldenVectorRunner(OtherPrefix).Run(PinnedFile(), Compute);

        Assert.False(report.Context.IsPinnedPrefix);
        Assert.Equal(2, report.Compared);
        Assert.Equal(2, report.Skipped);
        report.ThrowIfFailed();
    }

    [Fact]
    public void A_drifted_canonicalizer_is_reported_per_case_and_field()
    {
        var report = new GoldenVectorRunner(PinnedPrefix).Run(PinnedFile(), (vectorCase, context) =>
            new Dictionary<string, string> { [CanonicalField] = "AAAA" });

        Assert.Equal(4, report.Mismatches.Count);
        Assert.Contains(report.Mismatches, line => line.StartsWith($"get-record.{SignatureField}: expected", StringComparison.Ordinal) && line.EndsWith("got '<not produced>'", StringComparison.Ordinal));
        Assert.Throws<ContractViolationException>(report.ThrowIfFailed);
    }

    [Fact]
    public void A_run_that_compares_nothing_fails()
    {
        var report = new GoldenVectorRunner(OtherPrefix, new SkipEverything()).Run(PinnedFile(), Compute);

        Assert.True(report.ToCheckResult().IsVacuous);
        Assert.Throws<ContractViolationException>(report.ThrowIfFailed);
    }

    [Fact]
    public void Rebasing_works_on_text_fields_and_keeps_non_text_bytes()
    {
        var context = new VectorRunContext(OtherPrefix, PinnedPrefix, "k1");
        byte[] raw = [0xFF, .. Encoding.UTF8.GetBytes(PinnedPrefix + "-CANON-v1"), 0x00];
        byte[] rebased = [0xFF, .. Encoding.UTF8.GetBytes(OtherPrefix + "-CANON-v1"), 0x00];

        Assert.Equal($"{OtherPrefix}-SIG-v1", VectorExpectationRebaser.Default.Rebase("expectedCanonicalScheme", $"{PinnedPrefix}-SIG-v1", context));
        Assert.Equal(Convert.ToBase64String(rebased), VectorExpectationRebaser.Default.Rebase(CanonicalField, Convert.ToBase64String(raw), context));
        Assert.Null(VectorExpectationRebaser.Default.Rebase(SignatureField, "x", context));
    }

    private GoldenVectorFile PinnedFile()
    {
        var cases = new[] { ("get-record", "GET", "/records/1"), ("post-record", "POST", "/records") }.Select(entry =>
        {
            var canonical = Canonical(PinnedPrefix, entry.Item2, entry.Item3);
            return new Dictionary<string, string>
            {
                ["name"] = entry.Item1, ["method"] = entry.Item2, ["path"] = entry.Item3,
                [CanonicalField] = Convert.ToBase64String(canonical), [SignatureField] = Sign(canonical),
            };
        });
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, prefix = PinnedPrefix, fixtureKeyId = "fixture-1", cases });
        return GoldenVectorFile.Parse(json, "inline");
    }

    private Dictionary<string, string> Compute(GoldenVectorCase vectorCase, VectorRunContext context)
    {
        var canonical = Canonical(context.Prefix, vectorCase.InputString("method"), vectorCase.InputString("path"));
        return new Dictionary<string, string> { [CanonicalField] = Convert.ToBase64String(canonical), [SignatureField] = Sign(canonical) };
    }

    private static byte[] Canonical(string prefix, string method, string path) => Encoding.UTF8.GetBytes($"{prefix}-CANON-v1\n{method}\n{path}");

    private string Sign(byte[] canonical) => Convert.ToBase64String(HMACSHA256.HashData(_key, canonical));

    private sealed class SkipEverything : VectorExpectationRebaser
    {
        public override string? Rebase(string field, string pinnedValue, VectorRunContext context) => null;
    }
}
