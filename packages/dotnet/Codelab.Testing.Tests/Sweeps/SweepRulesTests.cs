using Codelab.Testing.Sweeps;

namespace Codelab.Testing.Tests.Sweeps;

/// <summary>Secret-shaped inputs are assembled at run time, so the repository itself stays clean.</summary>
public sealed class SweepRulesTests
{
    private const string Dashes = "-----";

    [Fact]
    public void Banned_literals_match_exactly_or_ignoring_case()
    {
        var exact = SweepRules.BannedLiterals("banned-api", ["Legacy.Clock", "OldThing"], "use TimeProvider");
        var folded = SweepRules.BannedLiterals("banned-api", ["OldThing"], "m", ignoreCase: true);

        Assert.Single(SourceSweep.SweepText("var t = Legacy.Clock.Read();", "a.cs", [exact]));
        Assert.Empty(SourceSweep.SweepText("var t = LegacyXClock.Read();", "a.cs", [exact]));
        Assert.Single(SourceSweep.SweepText("oldthing", "a.cs", [folded]));
        Assert.Throws<ArgumentException>(() => SweepRules.BannedLiterals("x", [" "], "m"));
    }

    [Fact]
    public void Brand_identifiers_match_as_words_and_inside_identifiers()
    {
        var rule = SweepRules.BrandIdentifiers(["Contoso"]);

        Assert.Single(SourceSweep.SweepText("Welcome to contoso", "a.md", [rule]));
        Assert.Single(SourceSweep.SweepText("class ContosoPortalClient {}", "a.cs", [rule]));
        Assert.Empty(SourceSweep.SweepText("contosoish", "a.md", [rule]));
    }

    public static TheoryData<string, string> SecretSamples() => new()
    {
        { "secret-private-key", Dashes + "BEGIN EC " + "PRIVATE KEY" + Dashes },
        { "secret-aws-access-key", "AKIA" + new string('Q', 16) },
        { "secret-github-token", "ghp_" + new string('a', 36) },
        { "secret-jwt", "eyJ" + new string('a', 10) + ".eyJ" + new string('b', 10) + "." + new string('c', 10) },
        { "secret-connection-string-password", "Server=localhost;Pass" + "word=" + "Hunter" + "42x;" },
        { "secret-url-credentials", "https://user:" + "s3cret" + "pass@example.test/" },
    };

    [Theory]
    [MemberData(nameof(SecretSamples))]
    public void Secret_shapes_are_found_and_redacted(string ruleId, string sample)
    {
        var finding = Assert.Single(SourceSweep.SweepText($"value = \"{sample}\"", "a.config", SweepRules.Secrets()));

        Assert.Equal(ruleId, finding.RuleId);
        Assert.EndsWith("****", finding.Excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain(sample, finding.Excerpt, StringComparison.Ordinal);
    }

    public static TheoryData<string> Placeholders() => new()
    {
        "Server=localhost;Pass" + "word={from-env};",
        "Server=localhost;Pass" + "word=$(DbPassword);",
        "https://example.test/path",
    };

    [Theory]
    [MemberData(nameof(Placeholders))]
    public void Placeholders_are_not_secrets(string line) =>
        Assert.Empty(SourceSweep.SweepText(line, "a.config", SweepRules.Secrets()));
}
