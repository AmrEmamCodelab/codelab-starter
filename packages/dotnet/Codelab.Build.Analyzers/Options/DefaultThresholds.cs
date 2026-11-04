using System.Collections.Immutable;

namespace Codelab.Build.Analyzers.Options;

/// <summary>
/// Fallback limits when no codelab_* key reaches a file (a consumer without the generated rules/codelab.globalconfig).
/// They mirror rules/base.rules.yaml; Codelab.Build.Analyzers.Tests fails when the two drift apart.
/// </summary>
internal static class DefaultThresholds
{
    public const int MethodLines = 40;
    public const int FileLines = 400;
    public const int Parameters = 5;
    public const bool ExcludeTests = true;
    public const bool MagicNumbersEnforced = true;

    public static readonly ImmutableArray<double> AllowedNumbers = ImmutableArray.Create(-1d, 0d, 1d);
}
