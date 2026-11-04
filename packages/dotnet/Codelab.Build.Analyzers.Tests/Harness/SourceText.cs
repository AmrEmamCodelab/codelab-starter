using System.Globalization;
using System.Text;

namespace Codelab.Build.Analyzers.Tests.Harness;

/// <summary>Builds C# sources of an exact size for the length analyzers.</summary>
internal static class SourceText
{
    /// <summary>
    /// A class with one void method <paramref name="name"/> whose block body (braces included) has exactly
    /// <paramref name="bodyLines"/> code lines; a blank and a comment line are added between statements and never count.
    /// </summary>
    public static string ClassWithMethod(string name, int bodyLines)
    {
        var source = new StringBuilder()
            .AppendLine("public class Sample")
            .AppendLine("{")
            .AppendLine(CultureInfo.InvariantCulture, $"    public void {name}()")
            .AppendLine("    {");
        for (var statement = 0; statement < bodyLines - 2; statement++)
        {
            source.AppendLine("        _ = 0;").AppendLine().AppendLine("        // not code");
        }

        return source.AppendLine("    }").AppendLine("}").ToString();
    }

    /// <summary>A file of exactly <paramref name="codeLines"/> code lines (at least 3): a class with int fields.</summary>
    public static string FileWithCodeLines(int codeLines)
    {
        const int ClassLines = 3;
        var source = new StringBuilder().AppendLine("// header comment").AppendLine("public class Sample").AppendLine("{");
        for (var field = 0; field < codeLines - ClassLines; field++)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"    public int Field{field};").AppendLine();
        }

        return source.AppendLine("}").ToString();
    }

    /// <summary>A comma-separated parameter list of <paramref name="count"/> int parameters.</summary>
    public static string Parameters(int count) =>
        string.Join(", ", Enumerable.Range(0, count).Select(index => $"int p{index}"));
}
