using Starter.DocsGen.Cli;
using Starter.DocsGen.Collection;

namespace Starter.DocsGen;

/// <summary>The console entry point (a named class: the global <c>Program</c> is the API host's).</summary>
internal static class DocsGenEntryPoint
{
    private static async Task<int> Main(string[] args) =>
        (int)await DocsGenCommand.RunAsync(
            arguments: args,
            source: new HostSurfaceCollector(),
            output: Console.Out,
            error: Console.Error,
            cancellationToken: CancellationToken.None);
}
