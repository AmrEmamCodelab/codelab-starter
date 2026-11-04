namespace Starter.SdkConsumer;

/// <summary>RS0030 fixture: both Thread.Sleep overloads are global banned APIs in Codelab.Build.Sdk.</summary>
public static class Waits
{
    /// <summary>Blocks the thread twice.</summary>
    public static void Pause()
    {
        Thread.Sleep(millisecondsTimeout: 0);
        Thread.Sleep(TimeSpan.Zero);
    }
}
