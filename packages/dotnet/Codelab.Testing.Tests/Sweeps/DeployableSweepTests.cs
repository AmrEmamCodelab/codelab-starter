using Codelab.Testing.Architecture;
using Codelab.Testing.Sweeps;
using Codelab.Testing.Tests.Support;

namespace Codelab.Testing.Tests.Sweeps;

public sealed class DeployableSweepTests
{
    // Sample terms are neutral words; the repository's real term list is read by path in the last test.
    private const string TermText = "legacy-brand/i: acme\n";
    private const string ExceptionText = "api/docs/** term-*\n";
    private const string TermFile = "tools/extract/sample-terms.txt";
    private const string ExceptionFile = "tools/extract/scrub-exceptions.txt";

    // Every deployable of the starter: the API, the attachment service, the integration gateway, the Ops Console
    // (API and web) and the web app. A new deployable is added here and gets its own sweep test in its test project.
    private static readonly string[] Deployables =
    [
        "backend",
        "services/attachment-service",
        "services/integration-gateway",
        "services/ops-console",
        "frontend/starter-web",
    ];

    [Fact]
    public void Included_directories_limit_the_walk_and_keep_paths_relative_to_the_root()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("api/src/Service.cs", "// acme\n");
        workspace.Write("other/Leak.cs", "acme\n");
        var options = new SourceSweepOptions { Root = workspace.Root, IncludedDirectories = ["api"] };

        var findings = SourceSweep.Run(options, SweepConfigurationFiles.ParseTermRules(TermText, "terms"));

        Assert.Equal("api/src/Service.cs:1: [term-legacy-brand] acme", Assert.Single(findings).ToString());
    }

    [Fact]
    public void A_missing_included_directory_is_refused()
    {
        using var workspace = new TempWorkspace();
        var options = new SourceSweepOptions { Root = workspace.Root, IncludedDirectories = ["no-such-folder"] };

        Assert.Throws<DirectoryNotFoundException>(() => SourceSweep.Check(options, SweepRules.Secrets()));
    }

    [Fact]
    public void A_deployable_with_a_term_or_a_secret_fails_and_repository_exceptions_apply()
    {
        using var workspace = SampleRepository();
        workspace.Write("api/src/Service.cs", "// acme\n");
        workspace.Write("api/docs/history.md", "acme was the old name\n");
        workspace.Write("api/appsettings.json", "\"Db\": \"Server=.;" + "Pass" + "word=hunter22;\"\n");
        workspace.Write("web/Leak.ts", "acme\n");

        var check = DeployableSweep.Check(["api"], startDirectory: workspace.Root);

        Assert.False(check.IsSuccessful);
        Assert.Equal(3, check.Examined);
        Assert.Equal(
            ["api/appsettings.json:1: [secret-connection-string-password] Pass****", "api/src/Service.cs:1: [term-legacy-brand] acme"],
            check.Violations);
    }

    [Fact]
    public void A_clean_deployable_passes()
    {
        using var workspace = SampleRepository();
        workspace.Write("api/src/Service.cs", "// fine\n");

        Assert.True(DeployableSweep.Check(["api"], startDirectory: workspace.Root).IsSuccessful);
    }

    [Fact]
    public void Local_only_files_and_folders_the_repository_ignores_are_not_read()
    {
        using var workspace = SampleRepository();
        workspace.Write("api/src/Service.cs", "// fine\n");
        workspace.Write("api/appsettings.Development.local.json", "acme\n");
        workspace.Write("api/coverage/report.html", "acme\n");
        workspace.Write("api/.env", "acme\n");

        var check = DeployableSweep.Check(["api"], startDirectory: workspace.Root);

        Assert.True(check.IsSuccessful);
        Assert.Equal(1, check.Examined);
    }

    [Fact]
    public void An_empty_deployable_fails_as_vacuous()
    {
        using var workspace = SampleRepository();
        workspace.Directory("api");

        Assert.True(DeployableSweep.Check(["api"], startDirectory: workspace.Root).IsVacuous);
    }

    [Fact]
    public void No_deployable_folder_is_refused()
    {
        using var workspace = SampleRepository();

        Assert.Throws<ArgumentException>(() => DeployableSweep.Check([], startDirectory: workspace.Root));
    }

    [Fact]
    public void The_extract_folder_must_hold_exactly_one_term_list()
    {
        using var workspace = SampleRepository();
        workspace.Write("tools/extract/second-terms.txt", TermText);

        Assert.Throws<InvalidOperationException>(() => DeployableSweep.Rules(workspace.Root));
    }

    [Fact]
    public void Every_deployable_of_the_repository_is_clean()
    {
        var root = RepositoryRoot.Find(CompiledRules.RelativePath);

        foreach (var deployable in Deployables)
        {
            DeployableSweep.Check([deployable], startDirectory: root).ThrowIfFailed();
        }
    }

    private static TempWorkspace SampleRepository()
    {
        var workspace = new TempWorkspace();
        workspace.Write(CompiledRules.RelativePath, "{}");
        workspace.Write(TermFile, TermText);
        workspace.Write(ExceptionFile, ExceptionText);
        return workspace;
    }
}
