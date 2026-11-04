using System.Text.Json;
using Codelab.Build.Analyzers.Options;
using Codelab.Build.Analyzers.Tests.Harness;
using Codelab.Build.Analyzers.Tests.SdkFixture;

namespace Codelab.Build.Analyzers.Tests;

public sealed class ThresholdReaderTests
{
    private static readonly double[] ParsedList = [-1d, 0d, 1.5d];

    [Fact]
    public void Without_any_key_the_defaults_apply()
    {
        var thresholds = ThresholdReader.Read(DictionaryOptions.Empty, DictionaryOptions.Empty);

        Assert.Equal(DefaultThresholds.MethodLines, thresholds.Sizes.MethodLines);
        Assert.Equal(DefaultThresholds.FileLines, thresholds.Sizes.FileLines);
        Assert.Equal(DefaultThresholds.Parameters, thresholds.Sizes.Parameters);
        Assert.Equal(DefaultThresholds.AllowedNumbers, thresholds.MagicNumbers.AllowedNumbers);
        Assert.True(thresholds.MagicNumbers.ExcludeTests);
        Assert.True(thresholds.MagicNumbers.Enforced);
    }

    [Fact]
    public void Tree_options_win_over_global_options_key_by_key()
    {
        var tree = Options((OptionKeys.MethodLines, "30"));
        var global = Options((OptionKeys.MethodLines, "50"), (OptionKeys.FileLines, "300"), (OptionKeys.ExcludeTests, "false"));

        var thresholds = ThresholdReader.Read(tree, global);

        Assert.Equal(30, thresholds.Sizes.MethodLines);
        Assert.Equal(300, thresholds.Sizes.FileLines);
        Assert.False(thresholds.MagicNumbers.ExcludeTests);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    [InlineData("")]
    public void A_size_that_is_not_a_positive_integer_falls_back_to_the_default(string value)
    {
        var thresholds = ThresholdReader.Read(Options((OptionKeys.Parameters, value)), DictionaryOptions.Empty);

        Assert.Equal(DefaultThresholds.Parameters, thresholds.Sizes.Parameters);
    }

    [Fact]
    public void Number_lists_parse_invariantly_and_reject_any_bad_entry()
    {
        Assert.Equal(ParsedList, ThresholdReader.ParseNumberList(" -1, 0 ,1.5")!.Value.ToArray());
        Assert.Null(ThresholdReader.ParseNumberList("1,two"));
    }

    [Fact]
    public void The_defaults_mirror_the_base_rules_in_rules_compiled_json()
    {
        using var compiled = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "rules", "rules.compiled.json")));
        var baseRules = compiled.RootElement.GetProperty("base");
        var thresholds = baseRules.GetProperty("thresholds");
        var magic = baseRules.GetProperty("required").EnumerateArray()
            .Single(rule => rule.GetProperty("id").GetString() == "base.no-magic-values")
            .GetProperty("settings");

        Assert.Equal(DefaultThresholds.MethodLines, thresholds.GetProperty("methodLines").GetInt32());
        Assert.Equal(DefaultThresholds.FileLines, thresholds.GetProperty("fileLines").GetInt32());
        Assert.Equal(DefaultThresholds.Parameters, thresholds.GetProperty("parameters").GetInt32());
        Assert.Equal(DefaultThresholds.AllowedNumbers, magic.GetProperty("allowedNumbers").EnumerateArray().Select(number => number.GetDouble()));
        Assert.Equal(DefaultThresholds.ExcludeTests, magic.GetProperty("excludeTests").GetBoolean());
    }

    private static DictionaryOptions Options(params (string Key, string Value)[] entries) =>
        new(entries.ToDictionary(entry => entry.Key, entry => entry.Value));
}
