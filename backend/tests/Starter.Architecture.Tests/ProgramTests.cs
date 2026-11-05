using Starter.Architecture.Tests.Support;

namespace Starter.Architecture.Tests;

/// <summary>
/// The host stays a composition root (build plan phase 2 exit criterion): Program.cs has at most 150 lines, so wiring
/// grows in the kernel's and modules' Add/Use extensions, never in the host.
/// </summary>
public sealed class ProgramTests
{
    /// <summary>The line limit of the host's Program.cs.</summary>
    public const int MaxProgramLines = 150;

    private const string SourceFolder = "backend/src";
    private const string ProgramFileName = "Program.cs";

    [Fact]
    public void ProgramCs_HasAtMost150Lines()
    {
        var hostFolder = StarterSolution.Host.GetName().Name!;
        var path = Path.Combine(StarterSolution.RepositoryRootPath, SourceFolder, hostFolder, ProgramFileName);

        var lines = File.ReadAllLines(path).Length;

        Assert.True(lines <= MaxProgramLines, $"{path} has {lines} lines; the limit is {MaxProgramLines}. Move wiring into an Add/Use extension.");
    }
}
