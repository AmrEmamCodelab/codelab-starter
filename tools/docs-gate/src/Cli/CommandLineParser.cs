namespace Starter.Tools.DocsGate.Cli;

/// <summary>Parses <c>check --root &lt;path&gt; [--base &lt;git-ref&gt;]</c>.</summary>
internal static class CommandLineParser
{
    public const string CheckCommand = "check";
    public const string RootOption = "--root";
    public const string BaseOption = "--base";
    public const string DefaultRoot = ".";

    public const string Usage =
        "Usage: docs-gate check --root <path> [--base <git-ref>]\n" +
        "  --root  repository root to check (default: current directory)\n" +
        "  --base  git ref for the module-changed check (default: working tree vs HEAD)";

    /// <summary>Returns the options, or an error message when the arguments are invalid.</summary>
    public static (CheckOptions? Options, string? Error) Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || !string.Equals(arguments[0], CheckCommand, StringComparison.Ordinal))
        {
            return (null, $"Expected the '{CheckCommand}' command.");
        }

        var root = DefaultRoot;
        string? baseRef = null;
        for (var index = 1; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (index + 1 >= arguments.Count)
            {
                return (null, $"Option '{name}' needs a value.");
            }

            var value = arguments[index + 1];
            switch (name)
            {
                case RootOption:
                    root = value;
                    break;
                case BaseOption:
                    baseRef = value;
                    break;
                default:
                    return (null, $"Unknown option '{name}'.");
            }
        }

        return (new CheckOptions(Root: Path.GetFullPath(root), BaseRef: baseRef), null);
    }
}
