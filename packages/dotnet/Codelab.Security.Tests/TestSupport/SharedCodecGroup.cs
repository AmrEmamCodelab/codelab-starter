using Codelab.Security.PublicIds;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>
/// Tests that install the process-wide PublicId codec (directly or by starting a host) run in this collection, one
/// at a time, so two keyrings never race for the global accessor.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedCodecGroup
{
    public const string Name = "Process-wide PublicId codec";
}

/// <summary>Installs a codec over a fresh ring for the lifetime of a test class.</summary>
internal sealed class InstalledCodec : IDisposable
{
    private readonly IDisposable _installation;

    public InstalledCodec(params string[] keyIds)
    {
        var ring = TestKeys.Ring(new PublicIdKeyringOptions(), keyIds.Length == 0 ? ["k1"] : keyIds);
        Keyring = PublicIdKeyring.Create(ring, TestProtocols.Test);
        Codec = new PublicIdCodec(Keyring, TestProtocols.Test);
        _installation = PublicIdCodecAccessor.Install(Codec);
    }

    public PublicIdKeyring Keyring { get; }

    public PublicIdCodec Codec { get; }

    public void Dispose() => _installation.Dispose();
}
