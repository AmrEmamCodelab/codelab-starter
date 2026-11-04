using System.Diagnostics.CodeAnalysis;

namespace Codelab.Security.PublicIds;

/// <summary>
/// The process-wide codec that <see cref="PublicId{TDomain}"/>, its JSON converter, model binder and minimal-API
/// <c>TryParse</c> use (they cannot take constructor dependencies). <c>UseCodelabSecurity()</c> installs it once at
/// start; a host that never installed one fails loudly on the first read or write instead of emitting numbers.
/// </summary>
public static class PublicIdCodecAccessor
{
    private const string NotInstalled =
        "No PublicId codec is installed. UseCodelabSecurity() installs it at start; a host that skipped it cannot read or write an opaque identifier.";

    private static volatile IPublicIdCodec? _installed;

    /// <summary>True when a codec is installed.</summary>
    public static bool IsInstalled => _installed is not null;

    /// <summary>The installed codec, or throws.</summary>
    /// <exception cref="InvalidOperationException">No codec is installed.</exception>
    public static IPublicIdCodec Codec => _installed ?? throw new InvalidOperationException(NotInstalled);

    /// <summary>Installs <paramref name="codec"/>; disposing the result restores the previous one (tests).</summary>
    public static IDisposable Install(IPublicIdCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        var previous = _installed;
        _installed = codec;
        return new Installation(previous);
    }

    /// <summary>Removes the installed codec until the result is disposed (tests of the not-installed path).</summary>
    internal static IDisposable Uninstall()
    {
        var previous = _installed;
        _installed = null;
        return new Installation(previous);
    }

    internal static bool TryGetCodec([NotNullWhen(true)] out IPublicIdCodec? codec)
    {
        codec = _installed;
        return codec is not null;
    }

    private sealed class Installation(IPublicIdCodec? previous) : IDisposable
    {
        private bool _restored;

        public void Dispose()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
            _installed = previous;
        }
    }
}
