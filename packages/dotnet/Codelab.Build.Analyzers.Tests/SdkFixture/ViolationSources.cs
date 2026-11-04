using System.Globalization;
using System.Text;

namespace Codelab.Build.Analyzers.Tests.SdkFixture;

/// <summary>Violating sources, one per gate; each breaks exactly one rule at the base thresholds (40/400/5).</summary>
internal static class ViolationSources
{
    private const string Namespace = "namespace Starter.SdkConsumer;";

    /// <summary>A method body of 41 code lines (braces included).</summary>
    public static string MethodOverLimit()
    {
        var source = new StringBuilder()
            .AppendLine(Namespace)
            .AppendLine("public static class LongMethod")
            .AppendLine("{")
            .AppendLine("    public static int Sum(int value)")
            .AppendLine("    {")
            .AppendLine("        var total = value;");
        for (var line = 0; line < 38; line++)
        {
            source.AppendLine("        total += value;");
        }

        return source.AppendLine("        return total;").AppendLine("    }").AppendLine("}").ToString();
    }

    /// <summary>A file of 401 code lines: a namespace, a class and 397 named constants.</summary>
    public static string FileOverLimit()
    {
        var source = new StringBuilder().AppendLine(Namespace).AppendLine("public static class Constants").AppendLine("{");
        for (var index = 0; index < 397; index++)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"    public const int Value{index} = {index};");
        }

        return source.AppendLine("}").ToString();
    }

    /// <summary>
    /// A hand-written violation from Fixtures/Violations. Those files break rules on purpose, so they live in a fixture
    /// folder that the content check skips and that this project does not compile.
    /// </summary>
    public static string FromFixture(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "packages", "dotnet", "Codelab.Build.Analyzers.Tests", "Fixtures", "Violations", fileName));
}
