using System.Text.Json;
using Codelab.Security.PublicIds;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.PublicIds;

[Collection(SharedCodecGroup.Name)]
public sealed class PublicIdJsonTests : IDisposable
{
    private readonly InstalledCodec _installed = new();

    public void Dispose() => _installed.Dispose();

    [Fact]
    public void An_identifier_is_written_as_its_token_and_reads_back_with_unconfigured_options()
    {
        var dto = new RecordDto(PublicId<Records>.From(42), Owner: null);

        var json = JsonSerializer.Serialize(dto);
        var back = JsonSerializer.Deserialize<RecordDto>(json);

        Assert.Contains(_installed.Codec.Protect(Records.Domain, 42), json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Id\":42", json, StringComparison.Ordinal);
        Assert.Equal(dto, back);
    }

    [Fact]
    public void Collections_dictionary_keys_and_nested_objects_all_carry_tokens()
    {
        var ids = new List<PublicId<Records>> { PublicId<Records>.From(1), PublicId<Records>.From(2) };
        var byId = new Dictionary<PublicId<Records>, string> { [PublicId<Records>.From(3)] = "x" };

        var json = JsonSerializer.Serialize(new { ids, byId });

        Assert.Contains(_installed.Codec.Protect(Records.Domain, 3), json, StringComparison.Ordinal);
        Assert.Equal(byId, JsonSerializer.Deserialize<Dictionary<PublicId<Records>, string>>(JsonSerializer.Serialize(byId)));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"42\"")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"m1_k1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    public void Anything_but_an_authentic_token_is_refused_with_one_message(string value)
    {
        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PublicId<Records>>(value));

        Assert.Equal(PublicIdFormat.UnreadableMessage, error.Message);
    }

    [Fact]
    public void A_token_of_another_family_is_refused()
    {
        var userToken = JsonSerializer.Serialize(PublicId<Users>.From(5));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PublicId<Records>>(userToken));
    }

    [Fact]
    public void A_nullable_identifier_reads_null_as_absent_and_still_refuses_a_number()
    {
        Assert.Null(JsonSerializer.Deserialize<PublicId<Users>?>("null"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PublicId<Users>?>("5"));
        Assert.Equal("null", JsonSerializer.Serialize<PublicId<Users>?>(null));
    }

    [Fact]
    public void An_absent_identifier_cannot_be_written_and_has_no_number()
    {
        Assert.False(default(PublicId<Records>).HasValue);
        Assert.Throws<InvalidOperationException>(() => default(PublicId<Records>).Value);
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(default(PublicId<Records>)));
        Assert.Equal("PublicId<record>()", default(PublicId<Records>).ToString());
    }

    [Fact]
    public void ToString_gives_the_token_never_the_number()
    {
        Assert.Equal(_installed.Codec.Protect(Records.Domain, 8), PublicId<Records>.From(8).ToString());
    }

    [Fact]
    public void The_factory_claims_identifiers_only()
    {
        var factory = new PublicIdJsonConverterFactory();

        Assert.True(factory.CanConvert(typeof(PublicId<Records>)));
        Assert.True(factory.CanConvert(typeof(PublicId<Records>?)));
        Assert.False(factory.CanConvert(typeof(long)));
        Assert.Throws<ArgumentException>(() => factory.CreateConverter(typeof(string), JsonSerializerOptions.Default));
    }

    [Fact]
    public void Wrapping_an_identifier_below_one_is_a_precondition_violation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PublicId<Records>.From(0));
    }

    private sealed record RecordDto([property: WireId] PublicId<Records> Id, PublicId<Users>? Owner);
}

[Collection(SharedCodecGroup.Name)]
public sealed class PublicIdWithoutCodecTests
{
    [Fact]
    public void A_host_that_installed_no_codec_cannot_read_or_write_an_identifier()
    {
        using var uninstalled = PublicIdCodecAccessor.Uninstall();
        Assert.False(PublicIdCodecAccessor.IsInstalled);
        Assert.Throws<InvalidOperationException>(() => PublicId<Records>.From(1).ToToken());
        Assert.Throws<InvalidOperationException>(() => PublicId<Records>.TryParse("x", out _));
    }
}
