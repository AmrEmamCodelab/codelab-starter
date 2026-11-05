using System.Text;
using Starter.DocsGen.Output;
using Starter.DocsGen.Tests.Fixtures;

namespace Starter.DocsGen.Tests;

public sealed class WriterTests
{
    private static readonly Dictionary<string, string> Documents = new(StringComparer.Ordinal)
    {
        ["a.md"] = "# A\n",
        ["b.json"] = "{}\n",
    };

    [Fact]
    public void Write_creates_the_folder_and_writes_utf8_without_bom()
    {
        using var folder = new TempFolder();

        var report = new GeneratedDocsWriter(folder.Path).Write(Documents);

        Assert.Equal(["a.md", "b.json"], report.Written);
        Assert.Equal(Encoding.UTF8.GetBytes("# A\n"), File.ReadAllBytes(folder.File("a.md")));
    }

    [Fact]
    public void A_second_write_changes_nothing()
    {
        using var folder = new TempFolder();
        var writer = new GeneratedDocsWriter(folder.Path);
        writer.Write(Documents);

        var report = writer.Write(Documents);

        Assert.Empty(report.Written);
        Assert.Equal(["a.md", "b.json"], report.Unchanged);
    }

    [Fact]
    public void Compare_is_clean_after_a_write()
    {
        using var folder = new TempFolder();
        var writer = new GeneratedDocsWriter(folder.Path);
        writer.Write(Documents);

        var report = writer.Compare(Documents);

        Assert.True(report.IsClean);
        Assert.Equal(2, report.UpToDate.Count);
    }

    [Fact]
    public void Compare_reports_missing_changed_and_unexpected_files()
    {
        using var folder = new TempFolder();
        var writer = new GeneratedDocsWriter(folder.Path);
        writer.Write(Documents);
        File.Delete(folder.File("a.md"));
        File.WriteAllText(folder.File("b.json"), "{}\r\n");
        File.WriteAllText(folder.File("stray.txt"), "x");

        var report = writer.Compare(Documents);

        Assert.Equal(
            [new FileDrift("a.md", DriftKind.Missing), new FileDrift("b.json", DriftKind.Changed), new FileDrift("stray.txt", DriftKind.Unexpected)],
            report.Drifts);
        Assert.False(report.IsClean);
    }

    [Fact]
    public void Compare_on_a_missing_folder_reports_every_file_missing_and_creates_nothing()
    {
        using var folder = new TempFolder();

        var report = new GeneratedDocsWriter(folder.Path).Compare(Documents);

        Assert.All(report.Drifts, drift => Assert.Equal(DriftKind.Missing, drift.Kind));
        Assert.Equal(2, report.Drifts.Count);
        Assert.False(Directory.Exists(folder.Path));
    }

    [Fact]
    public void Write_reports_but_never_deletes_a_file_it_does_not_generate()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Path);
        File.WriteAllText(folder.File("keep.txt"), "mine");

        var report = new GeneratedDocsWriter(folder.Path).Write(Documents);

        Assert.Equal(["keep.txt"], report.Unexpected);
        Assert.True(File.Exists(folder.File("keep.txt")));
    }

    [Theory]
    [InlineData((int)DriftKind.Missing, "missing:")]
    [InlineData((int)DriftKind.Changed, "changed:")]
    [InlineData((int)DriftKind.Unexpected, "unexpected:")]
    public void Drift_kinds_have_a_label(int kind, string label)
    {
        Assert.Equal(label, DriftKinds.Describe((DriftKind)kind));
    }

    [Fact]
    public void An_unknown_drift_kind_is_a_bug()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DriftKinds.Describe((DriftKind)42));
    }
}
