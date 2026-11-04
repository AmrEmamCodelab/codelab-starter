using Microsoft.CodeAnalysis;

namespace Codelab.Build.Analyzers;

/// <summary>The diagnostic descriptors. Every rule is an error by default; thresholds come from the codelab_* keys.</summary>
internal static class Descriptors
{
    private const string Category = "Maintainability";

    public static readonly DiagnosticDescriptor MethodTooLong = new(
        id: DiagnosticIds.MethodTooLong,
        title: "Method is too long",
        messageFormat: "'{0}' has {1} lines of code; the limit is {2} (codelab_method_lines, rule base.clean-code). Extract smaller single-purpose methods.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Counts the lines of a member body that hold code (braces included; blank and comment-only lines are not counted).");

    public static readonly DiagnosticDescriptor FileTooLong = new(
        id: DiagnosticIds.FileTooLong,
        title: "File is too long",
        messageFormat: "'{0}' has {1} lines of code; the limit is {2} (codelab_file_lines, rule base.clean-code). Split it by responsibility.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Counts the lines of a source file that hold code; blank and comment-only lines are not counted.");

    public static readonly DiagnosticDescriptor TooManyParameters = new(
        id: DiagnosticIds.TooManyParameters,
        title: "Too many parameters",
        messageFormat: "'{0}' has {1} parameters; the limit is {2} (codelab_parameters, rule base.clean-code). Group related values into a request or options type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Overrides, explicit interface implementations, extern methods and record primary constructors are not reported.");

    public static readonly DiagnosticDescriptor MagicNumber = new(
        id: DiagnosticIds.MagicNumber,
        title: "Magic number",
        messageFormat: "Magic number {0}: give it a name (const, static readonly field, options property or enum member) (rule base.no-magic-values). Allowed bare numbers: {1}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Numeric literals are allowed in const and readonly field initializers, property initializers, enum members, attribute arguments and parameter defaults.");

    public static readonly DiagnosticDescriptor EnumMemberWithoutValue = new(
        id: DiagnosticIds.EnumMemberWithoutValue,
        title: "Enum member without an explicit value",
        messageFormat: "'{0}.{1}' has no explicit value (rule base.enums-as-int). Enums are stored as integers: give every member a value, e.g. Draft = 1.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Reordering or inserting members silently changes the stored meaning of implicit values.");
}
