using Codelab.Testing.Sweeps;

namespace Codelab.Testing.Tests.Sweeps;

public sealed class GlobAndNormalizerTests
{
    [Theory]
    [InlineData("provenance/origins/a.json", "provenance/**", true)]
    [InlineData("docs/adr/ADR-001.md", "docs/adr/**", true)]
    [InlineData("docs/other/ADR-001.md", "docs/adr/**", false)]
    [InlineData("tools/extract/tests/fixtures/x/y.json", "tools/extract/tests/fixtures/**", true)]
    [InlineData("a/b/scrub-exceptions.txt", "scrub-exceptions.txt", true)]
    [InlineData("a/b/Thing.cs", "*.cs", true)]
    [InlineData("a/b/Thing.ts", "*.{cs,ts}", true)]
    [InlineData("a/b/Thing.md", "*.{cs,ts}", false)]
    [InlineData("src/a.cs", "src/*.cs", true)]
    [InlineData("src/x/a.cs", "src/*.cs", false)]
    [InlineData("src/x/a.cs", "src/**/a.cs", true)]
    [InlineData("src/a.cs", "src/**/a.cs", true)]
    [InlineData("src/ab.cs", "src/a?.cs", true)]
    [InlineData(@"src\win\a.cs", "src/win/*.cs", true)]
    [InlineData("./src/a.cs", "src/a.cs", true)]
    [InlineData("srcXa.cs", "src.a.cs", false)]
    public void Globs_follow_the_scanner_semantics(string path, string glob, bool expected) =>
        Assert.Equal(expected, GlobPattern.IsMatch(path, glob));

    [Theory]
    [InlineData("OrderId", "Order Id")]
    [InlineData("IApiClient", "I Api Client")]
    [InlineData("MAX_RETRY_COUNT", "MAX RETRY COUNT")]
    [InlineData("isSubmitted", "is Submitted")]
    [InlineData("Record2Copy", "Record 2 Copy")]
    [InlineData("plain text", "plain text")]
    [InlineData("", "")]
    public void Identifiers_are_split_on_case_digit_and_underscore(string line, string expected) =>
        Assert.Equal(expected, IdentifierNormalizer.Normalize(line));

    [Fact]
    public void Arabic_is_normalized_without_diacritics_and_with_one_alef()
    {
        const string withMarks = "أَحْمَدـ";
        const string withMaqsura = "موسى";

        Assert.Equal("احمد", IdentifierNormalizer.Normalize(withMarks));
        Assert.Equal("موسي", IdentifierNormalizer.Normalize(withMaqsura));
    }
}
