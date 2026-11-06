using Codelab.Testing.Checks;
using Starter.Architecture.Tests.Support;

namespace Starter.Architecture.Tests.Engine;

/// <summary>
/// The generic business engine stays generic (build-plan phase 7): (a) its code carries no sample or source-project
/// vocabulary, (b) nothing but the host and the sample's own tests references the deletable sample module.
/// </summary>
public sealed class EngineIndependenceTests
{
    private const string SampleFile = "backend/src/Modules/Records/Application/Sample.cs";
    private const string SampleProject = "backend/src/Modules/Records/Starter.Modules.Records.csproj";

    [Fact]
    public void The_generic_engine_code_names_no_sample_vocabulary()
    {
        var sources = EngineVocabularyScanner.ReadEngine(StarterSolution.RepositoryRootPath);

        var findings = EngineVocabularyScanner.Scan(sources, EngineVocabularyScanner.SampleRules);

        Assert.NotEmpty(sources);
        new CheckResult("Generic engine code names no sample vocabulary", findings, sources.Count).ThrowIfFailed();
    }

    [Fact]
    public void The_generic_engine_code_names_no_source_project_term()
    {
        var root = StarterSolution.RepositoryRootPath;
        var sources = EngineVocabularyScanner.ReadEngine(root);
        var rules = EngineVocabularyScanner.SourceTermRules(root);

        var findings = EngineVocabularyScanner.Scan(sources, rules);

        Assert.NotEmpty(rules);
        new CheckResult("Generic engine code names no source-project term", findings, sources.Count).ThrowIfFailed();
    }

    [Fact]
    public void The_sweep_covers_the_records_kernel() =>
        Assert.Contains(
            EngineVocabularyScanner.EngineFolders(StarterSolution.RepositoryRootPath),
            folder => folder.Replace('\\', '/').EndsWith("Starter.Application/Records", StringComparison.Ordinal));

    /// <summary>Lines the sample sweep must flag.</summary>
    public static TheoryData<string> SampleVocabulary => new()
    {
        "public sealed class ServiceRequestRules { }",
        "const string Type = \"service-request\";",
        "const string Key = \"serviceRequests.status.draft.staff\";",
        "const string Prefix = \"SR\";",
        "// for example SR-2026-001",
        "const string Lookup = \"service-request-categories\";",
    };

    [Theory]
    [MemberData(nameof(SampleVocabulary))]
    public void The_sample_sweep_flags_sample_vocabulary(string line)
    {
        var finding = Assert.Single(EngineVocabularyScanner.Scan([Source(line)], EngineVocabularyScanner.SampleRules));

        Assert.StartsWith($"{SampleFile}:1 ", finding, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed record RecordRef(long Id, string RecordType);")]
    [InlineData("const string Prefix = \"TR\"; // TR-2026-001")]
    [InlineData("var service = provider.GetRequiredService<IRecordLifecycle>(); var requests = 2;")]
    public void The_sample_sweep_passes_generic_code(string line) =>
        Assert.Empty(EngineVocabularyScanner.Scan([Source(line)], EngineVocabularyScanner.SampleRules));

    [Fact]
    public void A_source_term_is_found_as_written_and_inside_an_identifier()
    {
        // The term is built at run time so the repository scrub sweep does not flag this file.
        var term = string.Concat("Wid", "get");
        var rules = EngineVocabularyScanner.ParseTermRules(["# comment", string.Empty, $"domain-thing/i: \\b{term}s?\\b"]);

        var asWritten = EngineVocabularyScanner.Scan([Source($"// a {term.ToLowerInvariant()} list")], rules);
        var inIdentifier = EngineVocabularyScanner.Scan([Source($"long {term}Id = 1;")], rules);

        Assert.Equal("term-domain-thing", Assert.Single(rules).Id);
        Assert.Single(asWritten);
        Assert.Single(inIdentifier);
    }

    [Fact]
    public void A_term_rule_without_the_i_flag_is_case_sensitive()
    {
        var rules = EngineVocabularyScanner.ParseTermRules(["type-thing: \\bIThing\\b"]);

        Assert.Single(EngineVocabularyScanner.Scan([Source("IThing x;")], rules));
        Assert.Empty(EngineVocabularyScanner.Scan([Source("ithing x;")], rules));
    }

    [Fact]
    public void No_assembly_but_the_host_references_the_sample_module() =>
        new CheckResult(
            "Only the host references the sample module",
            SampleReferenceScanner.ForbiddenAssemblyReferences(StarterSolution.All),
            StarterSolution.All.Count).ThrowIfFailed();

    [Fact]
    public void No_project_but_the_host_and_the_sample_tests_references_the_sample_module()
    {
        var projects = SampleReferenceScanner.ReadProjects(StarterSolution.RepositoryRootPath);

        var findings = SampleReferenceScanner.ForbiddenProjectReferences(projects);

        Assert.NotEmpty(projects);
        new CheckResult("Only the host and the sample tests reference the sample module", findings, projects.Count).ThrowIfFailed();
    }

    [Fact]
    public void A_generic_project_referencing_the_sample_is_found()
    {
        var project = new ProjectSource(SampleProject, ProjectXml($"..\\{SampleReferenceScanner.SampleAssemblyName}\\{SampleReferenceScanner.SampleAssemblyName}.csproj"));

        Assert.Equal([SampleProject], SampleReferenceScanner.ForbiddenProjectReferences([project]));
    }

    [Fact]
    public void A_generic_project_referencing_the_sample_as_a_package_is_found()
    {
        var project = new ProjectSource(SampleProject, $"<Project><ItemGroup><PackageReference Include=\"{SampleReferenceScanner.SampleAssemblyName}\" /></ItemGroup></Project>");

        Assert.Single(SampleReferenceScanner.ForbiddenProjectReferences([project]));
    }

    [Theory]
    [InlineData("backend/src/Starter.Api/Starter.Api.csproj")]
    [InlineData("backend/tests/Modules/Starter.Modules.ServiceRequests.Tests/Starter.Modules.ServiceRequests.Tests.csproj")]
    public void The_host_and_the_sample_tests_may_reference_the_sample(string path)
    {
        var project = new ProjectSource(path, ProjectXml($"../../src/Modules/ServiceRequests/{SampleReferenceScanner.SampleAssemblyName}.csproj"));

        Assert.Empty(SampleReferenceScanner.ForbiddenProjectReferences([project]));
    }

    [Fact]
    public void A_project_whose_name_only_starts_with_the_sample_name_is_not_the_sample()
    {
        var project = new ProjectSource(SampleProject, ProjectXml($"../X/{SampleReferenceScanner.SampleAssemblyName}Extras.csproj"));

        Assert.Empty(SampleReferenceScanner.ForbiddenProjectReferences([project]));
    }

    private static EngineSource Source(string line) => new(SampleFile, line);

    private static string ProjectXml(string include) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"{include}\" /></ItemGroup></Project>";
}
