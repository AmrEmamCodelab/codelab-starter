using Starter.DocsGen.Collection;
using Starter.DocsGen.Rendering;
using Starter.DocsGen.Tests.Fixtures;

namespace Starter.DocsGen.Tests;

public sealed class StatusScannerTests
{
    private static readonly OwnerNaming Owners = new(typeof(Program).Assembly);

    [Fact]
    public void Int_enums_named_Status_are_read_with_their_members_by_value()
    {
        var statuses = StatusScanner.Scan([typeof(FixtureRunStatus).Assembly], Owners, new XmlDocumentation());

        var run = Assert.Single(statuses);
        Assert.Equal(nameof(FixtureRunStatus), run.Name);
        Assert.Equal(typeof(FixtureRunStatus).FullName, run.FullName);
        Assert.Equal(OwnerNaming.KernelOwner, run.Owner);
        Assert.Equal("A fixture run's state (scanned by the status tests).", run.Description);
        Assert.Equal([(1, "Queued"), (2, "Running"), (3, "Done")], run.Members.Select(member => (member.Value, member.Name)));
        Assert.Equal("Waiting to run.", run.Members[0].Description);
    }

    [Fact]
    public void Only_int_enums_whose_name_ends_with_Status_count()
    {
        Assert.True(StatusScanner.IsStatusEnum(typeof(FixtureRunStatus)));
        Assert.False(StatusScanner.IsStatusEnum(typeof(FixtureWideStatus)));
        Assert.False(StatusScanner.IsStatusEnum(typeof(FixtureClassStatus)));
        Assert.False(StatusScanner.IsStatusEnum(typeof(FixtureMode)));
    }

    [Fact]
    public void Package_assemblies_are_left_out_of_the_own_assemblies()
    {
        var host = typeof(Program).Assembly;
        var own = SolutionAssemblies.OwnAssemblies(SolutionAssemblies.Discover(host, Starter.Infrastructure.Modularity.ModuleCatalog.Discover(host)), host);

        Assert.Contains(own, assembly => assembly.GetName().Name == "Starter.Application");
        Assert.Contains(own, assembly => assembly.GetName().Name == "Starter.Modules.Users");
        Assert.DoesNotContain(own, assembly => assembly.GetName().Name!.StartsWith(SolutionAssemblies.PlatformPackagePrefix, StringComparison.Ordinal));
    }
}

public sealed class StatusesRendererTests
{
    [Fact]
    public void Statuses_are_grouped_by_owner_with_one_table_per_enum_sorted_by_value()
    {
        var markdown = StatusesRenderer.Render(SurfaceFixtures.Surface().Statuses);

        Assert.True(markdown.IndexOf("## Alpha", StringComparison.Ordinal) < markdown.IndexOf("## Sample", StringComparison.Ordinal));
        Assert.Contains("### `RunStatus`", markdown, StringComparison.Ordinal);
        Assert.Contains("`Sample.Domain.RunStatus`", markdown, StringComparison.Ordinal);
        Assert.Contains("A run's | state.", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("| 1 | `Queued` | Waiting. |", StringComparison.Ordinal) < markdown.IndexOf("| 3 | `Done` | Finished. |", StringComparison.Ordinal));
        Assert.Contains("No members.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void No_statuses_is_a_note() =>
        Assert.Contains("No status enums are declared.", StatusesRenderer.Render([]), StringComparison.Ordinal);

    [Fact]
    public void The_readme_lists_the_statuses_file() =>
        Assert.Contains(GeneratedFileNames.Statuses, ReadmeRenderer.Render(), StringComparison.Ordinal);
}
