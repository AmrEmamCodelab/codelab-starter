using Starter.DocsGen.Collection;
using Starter.DocsGen.Output;
using Starter.DocsGen.Rendering;

namespace Starter.DocsGen.Cli;

/// <summary>Parses the command line, collects the API surface, renders every file, then writes or checks them.</summary>
internal static class DocsGenCommand
{
    /// <summary>
    /// Runs DocsGen. A usage error and a host that cannot be read become exit codes; any other exception propagates
    /// (a bug, which fails the build loudly).
    /// </summary>
    public static async Task<DocsGenExitCode> RunAsync(
        IReadOnlyList<string> arguments,
        IApiSurfaceSource source,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var parsed = DocsGenArguments.Parse(arguments);
        if (parsed.HelpRequested)
        {
            await output.WriteLineAsync(DocsGenArguments.Usage);
            return DocsGenExitCode.Success;
        }

        if (parsed.Arguments is null)
        {
            await error.WriteLineAsync(parsed.Error);
            await error.WriteLineAsync(DocsGenArguments.Usage);
            return DocsGenExitCode.Usage;
        }

        ApiSurface surface;
        try
        {
            surface = await source.CollectAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            await error.WriteLineAsync($"docs-gen: the API host could not be read: {exception.Message}");
            return DocsGenExitCode.Failure;
        }

        var documents = GeneratedDocSet.Render(surface);
        var writer = new GeneratedDocsWriter(Path.GetFullPath(parsed.Arguments.OutputDirectory));
        return parsed.Arguments.Check
            ? await ReportDriftAsync(writer.Compare(documents), output, error)
            : await ReportWrittenAsync(writer.Write(documents), output);
    }

    private static async Task<DocsGenExitCode> ReportDriftAsync(DriftReport report, TextWriter output, TextWriter error)
    {
        if (report.IsClean)
        {
            await output.WriteLineAsync($"docs-gen check: OK ({report.UpToDate.Count} generated file(s) up to date).");
            return DocsGenExitCode.Success;
        }

        foreach (var drift in report.Drifts)
        {
            await error.WriteLineAsync($"docs-gen check: {DriftKinds.Describe(drift.Kind)} {drift.FileName}");
        }

        await error.WriteLineAsync($"docs-gen check: {report.Drifts.Count} file(s) drifted. Regenerate with: {GeneratedDocSet.RegenerateCommand}");
        return DocsGenExitCode.Drift;
    }

    private static async Task<DocsGenExitCode> ReportWrittenAsync(WriteReport report, TextWriter output)
    {
        foreach (var fileName in report.Written)
        {
            await output.WriteLineAsync($"docs-gen: wrote {fileName}");
        }

        foreach (var fileName in report.Unexpected)
        {
            await output.WriteLineAsync($"docs-gen: {fileName} is not a generated file; remove it or --check fails.");
        }

        await output.WriteLineAsync($"docs-gen: {report.Written.Count} written, {report.Unchanged.Count} unchanged.");
        return DocsGenExitCode.Success;
    }
}
