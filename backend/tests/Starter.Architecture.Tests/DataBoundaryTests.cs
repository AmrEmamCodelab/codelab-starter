using Codelab.Testing.Checks;
using Starter.Architecture.Tests.Support;

namespace Starter.Architecture.Tests;

/// <summary>
/// Guardrails that the per-module content rules cannot give: a module's SQL names only its own schemas (ADR-004 at the
/// database), and no backend file maps the scheduler dashboard or uses static scheduler storage (rules
/// <c>base.no-scheduler-dashboard-mapping</c> and <c>base.no-static-scheduler-storage</c>).
/// </summary>
public sealed class DataBoundaryTests
{
    // Samples are built at run time so the repository content rules do not flag this file.
    private const string LookupsSchema = "lookups";
    private const string PortalSchema = "portal";
    private const string PortalOwner = "Portal";
    private const string LookupsOwner = "Lookups";
    private const string SampleFile = "Modules/Portal/Infrastructure/Sample.cs";

    private static readonly Dictionary<string, string> SampleOwners = new(StringComparer.OrdinalIgnoreCase)
    {
        [LookupsSchema] = LookupsOwner,
        [PortalSchema] = PortalOwner,
    };

    [Fact]
    public void NoModuleSqlNamesAnotherModulesSchema()
    {
        var sources = SourceFiles.ReadBackend();
        var owners = CrossSchemaSqlScanner.SchemaOwners(StarterSolution.All, StarterSolution.ModuleAssemblies);

        var violations = CrossSchemaSqlScanner.Scan(sources, owners);

        Assert.NotEmpty(owners);
        new CheckResult("Module SQL names only the module's own schemas", violations, sources.Count).ThrowIfFailed();
    }

    [Fact]
    public void NoBackendFileMapsTheSchedulerDashboardOrUsesStaticStorage()
    {
        var sources = SourceFiles.ReadBackend();

        var violations = sources.SelectMany(SchedulerSurfaceScanner.Scan);

        new CheckResult("No scheduler dashboard or static scheduler storage", violations, sources.Count).ThrowIfFailed();
    }

    [Fact]
    public void ASchemaNameSplitIntoConstantsIsStillFound()
    {
        var source = Portal($$"""
            static class Q
            {
                const string Schema = "{{LookupsSchema}}";
                public const string Query = "SELECT [Id] FROM [" + Schema + "].[Items]";
            }
            """);

        var finding = Assert.Single(CrossSchemaSqlScanner.Scan([source], SampleOwners));

        Assert.EndsWith($"{PortalOwner} -> {LookupsSchema}", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void ASchemaNameFromAConstantInAnotherFileIsFound()
    {
        var owner = new OwnedSource("Modules/Lookups/Db.cs", LookupsOwner, $$"""public static class LookupsDb { public const string SchemaName = "{{LookupsSchema}}"; }""");
        var reader = Portal("static class Q { static string Query(string table) => $\"SELECT 1 FROM {LookupsDb.SchemaName}.{table}\"; }");

        var finding = Assert.Single(CrossSchemaSqlScanner.Scan([owner, reader], SampleOwners));

        Assert.StartsWith(SampleFile, finding, StringComparison.Ordinal);
    }

    /// <summary>Strings that must not be findings for a Portal file (built here so the content rules do not see them).</summary>
    public static TheoryData<string> PassingSamples => new()
    {
        $"static class Q {{ const string Own = \"SELECT 1 FROM [{PortalSchema}].[Messages]\"; }}",
        $"static class Q {{ const string Permission = \"{LookupsSchema}.view\"; const string Route = \"{LookupsSchema}\"; }}",
        $"static class Q {{ // SELECT 1 FROM [{LookupsSchema}].[Items]{Environment.NewLine} }}",
    };

    [Theory]
    [MemberData(nameof(PassingSamples))]
    public void OwnSchemasPermissionKeysRoutesAndCommentsPass(string text) =>
        Assert.Empty(CrossSchemaSqlScanner.Scan([Portal(text)], SampleOwners));

    [Fact]
    public void TheKernelOwnsEveryFileOutsideModules()
    {
        Assert.Equal(PortalOwner, SourceFiles.OwnerOf("Modules/Portal/Api/X.cs"));
        Assert.Equal(SourceFiles.KernelOwner, SourceFiles.OwnerOf("Starter.Infrastructure/X.cs"));
        Assert.Equal(SourceFiles.KernelOwner, SourceFiles.OwnerOf("Modules/README.cs"));
    }

    [Fact]
    public void ADashboardMappedAtACustomPathIsFound()
    {
        var call = SchedulerSurfaceScanner.BannedMembers[0];
        var source = Portal($"static class Host {{ static void Wire(dynamic app) => app.{call}(\"/ops/jobs\"); }}");

        var finding = Assert.Single(SchedulerSurfaceScanner.Scan(source));

        Assert.EndsWith(call, finding, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticSchedulerStorageIsFound()
    {
        var access = string.Concat("Job", "Storage.Current");
        var source = Portal($"static class Host {{ static object Storage() => Hangfire.{access}; }}");

        Assert.Single(SchedulerSurfaceScanner.Scan(source));
    }

    [Fact]
    public void AMentionInACommentOrAStringIsNotASchedulerFinding()
    {
        var call = SchedulerSurfaceScanner.BannedMembers[0];
        var source = Portal($"static class Host {{ // {call}\n const string Note = \"{call}\"; }}");

        Assert.Empty(SchedulerSurfaceScanner.Scan(source));
    }

    private static OwnedSource Portal(string text) => new(SampleFile, PortalOwner, text);
}
