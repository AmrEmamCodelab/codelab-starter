namespace Codelab.OpsConsole.Api.Tests.TestSupport;

/// <summary>One shared console host per test class (its fakes record calls; tests assert on deltas or their own codes).</summary>
public sealed class OpsConsoleHostFixture : IDisposable
{
    internal OpsConsoleHost Host { get; } = new();

    public void Dispose() => Host.Dispose();
}
