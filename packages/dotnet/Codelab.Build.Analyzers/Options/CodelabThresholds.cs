using System.Collections.Immutable;

namespace Codelab.Build.Analyzers.Options;

/// <summary>The effective clean-code limits for one syntax tree.</summary>
internal sealed class CodelabThresholds
{
    public CodelabThresholds(SizeLimits sizes, MagicNumberPolicy magicNumbers)
    {
        Sizes = sizes;
        MagicNumbers = magicNumbers;
    }

    public SizeLimits Sizes { get; }

    public MagicNumberPolicy MagicNumbers { get; }
}

/// <summary>Size limits: a value above the limit is reported (equal is allowed).</summary>
internal sealed class SizeLimits
{
    public SizeLimits(int methodLines, int fileLines, int parameters)
    {
        MethodLines = methodLines;
        FileLines = fileLines;
        Parameters = parameters;
    }

    public int MethodLines { get; }

    public int FileLines { get; }

    public int Parameters { get; }
}

/// <summary>Which bare numbers are allowed and whether test projects and this tree are checked at all.</summary>
internal sealed class MagicNumberPolicy
{
    public MagicNumberPolicy(ImmutableArray<double> allowedNumbers, bool excludeTests, bool enforced)
    {
        AllowedNumbers = allowedNumbers;
        ExcludeTests = excludeTests;
        Enforced = enforced;
    }

    public ImmutableArray<double> AllowedNumbers { get; }

    public bool ExcludeTests { get; }

    public bool Enforced { get; }

    public bool IsAllowed(double value) => AllowedNumbers.Contains(value);
}
