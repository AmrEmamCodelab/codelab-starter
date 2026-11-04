using Codelab.Security.Tests.Keys;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// Where the golden vectors live and how a run is steered from outside (tools/vectors/verify-vectors.mjs):
/// <list type="bullet">
/// <item><c>CODELAB_REGEN_VECTORS=1</c>: rewrite the checked-in <c>packages/vectors/*.vectors.json</c> (prefix <c>CODELABTEST</c>).</item>
/// <item><c>CODELAB_VECTORS_OUT=&lt;dir&gt;</c> + <c>CODELAB_VECTOR_PREFIX=&lt;P&gt;</c>: write a vector set for another prefix there.</item>
/// </list>
/// </summary>
internal static class VectorEnvironment
{
    /// <summary>Set to <see cref="Enabled"/> to rewrite the checked-in vector files.</summary>
    public const string RegenerateVariable = "CODELAB_REGEN_VECTORS";

    /// <summary>A folder to write a vector set into (with <see cref="PrefixVariable"/>).</summary>
    public const string OutputVariable = "CODELAB_VECTORS_OUT";

    /// <summary>The protocol prefix of a generated set (default <see cref="TestProtocols.AlternatePrefix"/>).</summary>
    public const string PrefixVariable = "CODELAB_VECTOR_PREFIX";

    /// <summary>The value that switches <see cref="RegenerateVariable"/> on.</summary>
    public const string Enabled = "1";

    /// <summary>The xunit trait the vector tests carry (<c>--filter Category=GoldenVectors</c>).</summary>
    public const string TraitName = "Category";

    /// <summary>The trait value.</summary>
    public const string TraitValue = "GoldenVectors";

    private const string VectorsFolder = "packages/vectors";
    private const string FixtureKeysFolder = "packages/vectors/fixture-keys";

    /// <summary>The checked-in vector folder.</summary>
    public static string VectorsDirectory => PublishedMaterialTests.RepositoryFile(VectorsFolder);

    /// <summary>The checked-in fixture-key folder.</summary>
    public static string FixtureKeysDirectory => PublishedMaterialTests.RepositoryFile(FixtureKeysFolder);

    /// <summary>True when the checked-in files are to be rewritten.</summary>
    public static bool RegenerationRequested =>
        string.Equals(Environment.GetEnvironmentVariable(RegenerateVariable), Enabled, StringComparison.Ordinal);

    /// <summary>The requested output folder, or null.</summary>
    public static string? OutputDirectory => NonEmpty(Environment.GetEnvironmentVariable(OutputVariable));

    /// <summary>The requested prefix for a generated set, or the alternate test prefix.</summary>
    public static string GeneratedPrefix => NonEmpty(Environment.GetEnvironmentVariable(PrefixVariable)) ?? TestProtocols.AlternatePrefix;

    /// <summary>The path of one vector file in <paramref name="directory"/>.</summary>
    public static string FileIn(string directory, IVectorArea area) => Path.Combine(directory, area.Name + VectorGenerator.FileSuffix);

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
