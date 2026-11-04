namespace Starter.Tools.DocsGate.Cli;

/// <summary>Process exit codes. CI treats anything other than <see cref="Passed"/> as a failed step.</summary>
internal static class ExitCodes
{
    public const int Passed = 0;
    public const int FindingsReported = 1;
    public const int UsageOrToolError = 2;
}
