using Codelab.Testing.Vectors;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// The C# half of the golden vectors (the TypeScript half is <c>packages/npm/ng-security/src/vectors</c>; the
/// cross-language check is <c>node tools/vectors/verify-vectors.mjs --prefix &lt;P&gt;</c>).
/// </summary>
[Trait(VectorEnvironment.TraitName, VectorEnvironment.TraitValue)]
public sealed class GoldenVectorTests
{
    private const string LineFeed = "\n";
    private const string CarriageReturnLineFeed = "\r\n";

    public static TheoryData<string> AreaNames => new(VectorGenerator.Areas.Select(area => area.Name));

    // A prefix of another length proves the length prefixes move with it.
    private const string ShortPrefix = "ZX9";

    public static TheoryData<string> SecondPrefixes => new(new[] { VectorEnvironment.GeneratedPrefix, ShortPrefix }.Distinct(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(AreaNames))]
    public void Checked_in_vectors_match_this_implementation_byte_for_byte(string areaName)
    {
        var area = Area(areaName);
        var file = GoldenVectorFile.Load(VectorEnvironment.FileIn(VectorEnvironment.VectorsDirectory, area));

        var report = Run(area, file);

        Assert.Equal(SecurityProtocol.TestPrefix, file.Prefix);
        Assert.Equal(area.FixtureKeyId(FixtureKeySet.Default), file.FixtureKeyId);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(file.Cases.Sum(vectorCase => vectorCase.Expected.Count), report.Compared);
    }

    [Theory]
    [MemberData(nameof(AreaNames))]
    public void Checked_in_files_are_exactly_what_the_generator_writes(string areaName)
    {
        var area = Area(areaName);
        var checkedIn = File.ReadAllText(VectorEnvironment.FileIn(VectorEnvironment.VectorsDirectory, area)).Replace(CarriageReturnLineFeed, LineFeed, StringComparison.Ordinal);

        var generated = VectorGenerator.Generate(area, VectorContext.For(SecurityProtocol.TestPrefix));

        Assert.True(string.Equals(checkedIn, generated, StringComparison.Ordinal), $"{area.Name}{VectorGenerator.FileSuffix} is stale: the case catalogue or the implementation changed. Regenerate with {VectorEnvironment.RegenerateVariable}={VectorEnvironment.Enabled} and review the diff.");
    }

    [Theory]
    [MemberData(nameof(AreaNames))]
    public void Case_names_are_unique_and_every_case_expects_something(string areaName)
    {
        var file = GoldenVectorFile.Load(VectorEnvironment.FileIn(VectorEnvironment.VectorsDirectory, Area(areaName)));

        Assert.Equal(file.Cases.Count, file.Cases.Select(vectorCase => vectorCase.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(file.Cases, vectorCase => Assert.NotEmpty(vectorCase.Expected));
    }

    [Fact]
    public void Verify_cases_cover_every_verdict()
    {
        foreach (var area in VectorGenerator.Areas.Where(area => area is RequestSignatureVectors or ResponseSignatureVectors))
        {
            var verdicts = GoldenVectorFile.Load(VectorEnvironment.FileIn(VectorEnvironment.VectorsDirectory, area)).Cases
                .Select(vectorCase => vectorCase.Expected.GetValueOrDefault(CaseFields.ExpectedVerdict))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal([Verdicts.Malformed, Verdicts.Mismatch, Verdicts.Valid], verdicts.Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The generator entry point. <c>CODELAB_REGEN_VECTORS=1</c> rewrites <c>packages/vectors</c>;
    /// <c>CODELAB_VECTORS_OUT</c> (+ <c>CODELAB_VECTOR_PREFIX</c>) writes a set for another prefix there. With neither,
    /// it writes the test-prefix set to a temporary folder and runs it, so the path is exercised on every run.
    /// </summary>
    [Fact]
    public void Generator_writes_a_complete_vector_set()
    {
        var (directory, prefix, temporary) = OutputTarget();
        try
        {
            var written = VectorGenerator.WriteAll(directory, VectorContext.For(prefix));

            Assert.Equal(VectorGenerator.Areas.Count, written.Count);
            Assert.All(VectorGenerator.Areas, area => Run(area, GoldenVectorFile.Load(VectorEnvironment.FileIn(directory, area))));
        }
        finally
        {
            if (temporary)
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [MemberData(nameof(SecondPrefixes))]
    public void A_second_prefix_gets_its_own_bytes_and_the_same_verdicts(string prefix)
    {
        var directory = Directory.CreateTempSubdirectory("codelab-vectors-").FullName;
        try
        {
            VectorGenerator.WriteAll(directory, VectorContext.For(prefix));

            foreach (var area in VectorGenerator.Areas)
            {
                var generated = GoldenVectorFile.Load(VectorEnvironment.FileIn(directory, area));
                var pinned = GoldenVectorFile.Load(VectorEnvironment.FileIn(VectorEnvironment.VectorsDirectory, area));
                Assert.Equal(prefix, generated.Prefix);
                Run(area, generated);
                PrefixBinding.AssertBound(pinned, generated);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IVectorArea Area(string name) => VectorGenerator.Areas.Single(area => area.Name == name);

    private static GoldenVectorReport Run(IVectorArea area, GoldenVectorFile file)
    {
        var report = new GoldenVectorRunner(file.Prefix).Run(file, (vectorCase, run) => area.Compute(vectorCase, VectorContext.For(run.Prefix)));
        report.ThrowIfFailed();
        return report;
    }

    private static (string Directory, string Prefix, bool Temporary) OutputTarget()
    {
        if (VectorEnvironment.RegenerationRequested)
        {
            return (VectorEnvironment.VectorsDirectory, SecurityProtocol.TestPrefix, false);
        }

        return VectorEnvironment.OutputDirectory is { } requested
            ? (requested, VectorEnvironment.GeneratedPrefix, false)
            : (Directory.CreateTempSubdirectory("codelab-vectors-").FullName, SecurityProtocol.TestPrefix, true);
    }
}
