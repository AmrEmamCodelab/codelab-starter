using Microsoft.Extensions.Options;

namespace Codelab.Testing.Tests.Fixtures.Purity.Impure;

/// <summary>Options the impure port leaks.</summary>
public sealed class ImpureSettings
{
    public int Size { get; set; }
}

/// <summary>A port that leaks configuration into its signature, hidden inside a generic argument.</summary>
public interface IImpureCanonicalizer
{
    Task<IOptions<ImpureSettings>> SettingsAsync();
}
