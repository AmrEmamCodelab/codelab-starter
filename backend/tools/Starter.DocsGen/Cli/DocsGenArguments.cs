namespace Starter.DocsGen.Cli;

/// <summary>The parsed command line: <c>--out &lt;dir&gt; [--check]</c>.</summary>
/// <param name="OutputDirectory">The folder the generated files belong in (a relative path resolves against the current directory).</param>
/// <param name="Check">True to compare instead of write.</param>
internal sealed record DocsGenArguments(string OutputDirectory, bool Check)
{
    /// <summary>The output folder flag.</summary>
    public const string OutFlag = "--out";

    /// <summary>The drift-check flag.</summary>
    public const string CheckFlag = "--check";

    /// <summary>The help flag.</summary>
    public const string HelpFlag = "--help";

    /// <summary>The usage line.</summary>
    public const string Usage = "Usage: dotnet run --project backend/tools/Starter.DocsGen -- " + OutFlag + " <dir> [" + CheckFlag + "]";

    private const string FlagPrefix = "--";

    /// <summary>Parses <paramref name="arguments"/>; an unknown or repeated flag and a missing folder are usage errors.</summary>
    public static ArgumentParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? outputDirectory = null;
        var check = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case HelpFlag:
                    return ArgumentParseResult.Help();
                case CheckFlag when !check:
                    check = true;
                    break;
                case OutFlag when outputDirectory is null && HasValueAfter(arguments, index):
                    index++;
                    outputDirectory = arguments[index];
                    break;
                case OutFlag:
                    return ArgumentParseResult.Failed($"{OutFlag} needs exactly one folder.");
                default:
                    return ArgumentParseResult.Failed($"Unknown or repeated argument: {argument}");
            }
        }

        return string.IsNullOrWhiteSpace(outputDirectory)
            ? ArgumentParseResult.Failed($"{OutFlag} <dir> is required.")
            : ArgumentParseResult.Parsed(new DocsGenArguments(outputDirectory, check));
    }

    private static bool HasValueAfter(IReadOnlyList<string> arguments, int index) =>
        index + 1 < arguments.Count && !arguments[index + 1].StartsWith(FlagPrefix, StringComparison.Ordinal);
}

/// <summary>The outcome of <see cref="DocsGenArguments.Parse"/>: arguments, a usage error, or a help request.</summary>
internal sealed record ArgumentParseResult(DocsGenArguments? Arguments, string? Error, bool HelpRequested)
{
    /// <summary>Valid arguments.</summary>
    public static ArgumentParseResult Parsed(DocsGenArguments arguments) => new(arguments, Error: null, HelpRequested: false);

    /// <summary>A usage error.</summary>
    public static ArgumentParseResult Failed(string error) => new(Arguments: null, error, HelpRequested: false);

    /// <summary>A help request.</summary>
    public static ArgumentParseResult Help() => new(Arguments: null, Error: null, HelpRequested: true);
}
