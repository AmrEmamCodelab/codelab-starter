using Codelab.Build.Analyzers.Measurement;
using Microsoft.CodeAnalysis.CSharp;

namespace Codelab.Build.Analyzers.Tests;

public sealed class CodeLineCounterTests
{
    [Fact]
    public void Only_lines_with_tokens_count()
    {
        const string Source = """
            // leading comment

            /* block
               comment */
            #region Members
            public class Sample
            {
                /// <summary>Doc comment.</summary>
                public int Value; // trailing comment
            #if NEVER
                public int Disabled;
            #endif
            }
            #endregion
            """;

        Assert.Equal(4, Count(Source));
    }

    [Fact]
    public void A_token_spanning_lines_counts_every_line_it_covers()
    {
        const string Source = "public class Sample\n{\n    public string Text = @\"one\ntwo\nthree\";\n}\n";

        Assert.Equal(6, Count(Source));
    }

    private static int Count(string source) =>
        CodeLineCounter.Count(CSharpSyntaxTree.ParseText(source).GetRoot());
}
