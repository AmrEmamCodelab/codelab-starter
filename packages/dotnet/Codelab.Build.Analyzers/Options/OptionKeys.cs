namespace Codelab.Build.Analyzers.Options;

/// <summary>
/// The .editorconfig / globalconfig keys the rules engine emits (tools/rules-engine, emitters/editorconfig.mjs and
/// emitters/globalconfig.mjs). Keys are matched lower-case, as Roslyn normalizes them.
/// </summary>
public static class OptionKeys
{
    /// <summary>Maximum code lines in a member body (thresholds.methodLines).</summary>
    public const string MethodLines = "codelab_method_lines";

    /// <summary>Maximum code lines in a file (thresholds.fileLines).</summary>
    public const string FileLines = "codelab_file_lines";

    /// <summary>Maximum parameters of a method, constructor or delegate (thresholds.parameters).</summary>
    public const string Parameters = "codelab_parameters";

    /// <summary>Comma-separated numbers allowed as bare literals (base.no-magic-values settings.allowedNumbers).</summary>
    public const string AllowedNumbers = "codelab_allowed_numbers";

    /// <summary>true: test projects are not checked for magic numbers (base.no-magic-values settings.excludeTests).</summary>
    public const string ExcludeTests = "codelab_exclude_tests";

    /// <summary>false only when the module exempts base.no-magic-values (a reviewed module.rules.yaml exemption).</summary>
    public const string MagicNumbersEnforced = "codelab_magic_numbers_enforced";

    /// <summary>MSBuild MSBuildProjectName, made visible to analyzers by Codelab.Build.Sdk (CompilerVisibleProperty).</summary>
    public const string ProjectNameProperty = "build_property.MSBuildProjectName";
}
