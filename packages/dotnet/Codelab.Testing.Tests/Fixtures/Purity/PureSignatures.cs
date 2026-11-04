namespace Codelab.Testing.Tests.Fixtures.Purity.Pure;

/// <summary>A port whose signatures mention only plain types.</summary>
public interface IPureCanonicalizer
{
    Task<byte[]> CanonicalizeAsync(string method, IReadOnlyList<string> parts);
}
