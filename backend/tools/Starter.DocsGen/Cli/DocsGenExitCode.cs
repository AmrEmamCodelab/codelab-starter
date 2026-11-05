namespace Starter.DocsGen.Cli;

/// <summary>The process exit codes of DocsGen.</summary>
internal enum DocsGenExitCode
{
    /// <summary>The files were written, or <c>--check</c> found them up to date.</summary>
    Success = 0,

    /// <summary><c>--check</c> found at least one missing, changed or unexpected file.</summary>
    Drift = 1,

    /// <summary>The command line is wrong.</summary>
    Usage = 2,

    /// <summary>The API host could not be started or read.</summary>
    Failure = 3,
}
