using Codelab.Observability;
using Codelab.Security;
using Package.Sample.Tests.Support;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>
/// Exit criterion: a Production boot configured with a published key fails fast. The keys used are the starter's own
/// TEST-ONLY fixture keys, which Codelab.Security carries on its published-material denylist.
/// </summary>
public sealed class PublishedKeyBootTests
{
    private const string PublishedMarker = "PUBLISHED";
    private const string LineFeed = "\n";
    private const string CarriageReturnLineFeed = "\r\n";

    public static TheoryData<string, string> PublishedRings => new()
    {
        { SecuritySections.RequestSigning, FixtureKeys.SigningFile },
        { SecuritySections.PublicIds, FixtureKeys.PublicIdFile },
    };

    [Theory]
    [MemberData(nameof(PublishedRings))]
    public void A_fixture_key_in_a_production_ring_refuses_to_start(string section, string fixtureFile)
    {
        var (keyId, material) = FixtureKeys.ActiveRingKey(fixtureFile);
        using var host = new SampleHost(SampleKeys.Create(), settings => SampleSettings.SetRing(settings, section, keyId, material));

        var failure = Assert.ThrowsAny<Exception>(() => host.NewClient());

        var messages = string.Join(Environment.NewLine, Chain(failure).Select(exception => exception.Message));
        Assert.Contains(PublishedMarker, messages, StringComparison.Ordinal);
        Assert.DoesNotContain(material, messages, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_service_scheme_trusting_the_fixture_public_key_refuses_to_start(bool crlf)
    {
        var pem = FixtureKeys.ServicePublicKeyPem().ReplaceLineEndings(crlf ? CarriageReturnLineFeed : LineFeed);
        using var host = new SampleHost(SampleKeys.Create(), settings => settings[$"{SecuritySections.ServiceAssertionServer}:TrustedServices:0:PublicKeyPem"] = pem);

        var failure = Assert.ThrowsAny<Exception>(() => host.NewClient());

        Assert.Contains(PublishedMarker, string.Join(Environment.NewLine, Chain(failure).Select(exception => exception.Message)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_fixture_key_as_the_user_hash_key_refuses_to_start()
    {
        var (_, material) = FixtureKeys.ActiveRingKey(FixtureKeys.SigningFile);
        using var host = new SampleHost(SampleKeys.Create(), settings => settings[UserIdHashSettings.KeyName] = material);

        var failure = Assert.ThrowsAny<Exception>(() => host.NewClient());

        var messages = string.Join(Environment.NewLine, Chain(failure).Select(exception => exception.Message));
        Assert.Contains(PublishedMarker, messages, StringComparison.Ordinal);
        Assert.DoesNotContain(material, messages, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_configuration_with_fresh_keys_starts()
    {
        using var host = new SampleHost(SampleKeys.Create());

        using var client = host.NewClient();

        Assert.NotNull(client);
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Chain))
                {
                    yield return inner;
                }
            }
        }
    }
}
