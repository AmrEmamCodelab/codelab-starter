namespace Starter.Integration.Tests.Support;

/// <summary>Run-unique name parts, so tests sharing one host database never collide.</summary>
public static class TestNames
{
    /// <summary>The length of <see cref="Suffix"/>.</summary>
    public const int SuffixLength = 8;

    /// <summary>A short lowercase hex suffix, unique per call in practice.</summary>
    public static string Suffix() => Guid.NewGuid().ToString("N")[..SuffixLength];
}
