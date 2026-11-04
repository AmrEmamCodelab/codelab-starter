using System.Net;
using System.Text.Json;
using Codelab.Errors;
using Codelab.Security.PublicIds;
using Package.Sample.Api.Hosting;
using Package.Sample.Api.Records;
using Package.Sample.Tests.Support;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>Exit criterion: PublicId round trip, and tampered, foreign-family and numeric ids refused.</summary>
public sealed class PublicIdTests : IDisposable
{
    private readonly SampleHost _host = new(SampleKeys.Create());

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_record_id_round_trips_as_an_opaque_token()
    {
        using var client = _host.NewClient();
        var token = PublicId<RecordIds>.From(RecordStore.SeedRecordId).ToToken();

        var response = await client.GetAsync(SampleRoutes.RecordFor(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var wireId = body.RootElement.GetProperty("id");
        Assert.Equal(JsonValueKind.String, wireId.ValueKind);
        Assert.DoesNotContain(RecordStore.SeedRecordId.ToString(System.Globalization.CultureInfo.InvariantCulture), wireId.GetString()!, StringComparison.Ordinal);
        Assert.True(PublicId<RecordIds>.TryParse(wireId.GetString(), out var readBack));
        Assert.Equal(RecordStore.SeedRecordId, readBack.Value);
    }

    [Fact]
    public async Task Tampered_foreign_and_numeric_ids_are_refused_with_the_error_envelope()
    {
        using var client = _host.NewClient();
        var token = PublicId<RecordIds>.From(RecordStore.SeedRecordId).ToToken();
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');
        var foreign = PublicId<WidgetIds>.From(RecordStore.SeedRecordId).ToToken();

        foreach (var bad in new[] { tampered, foreign, "42" })
        {
            var problem = await Problems.ReadAsync(await client.GetAsync(SampleRoutes.RecordFor(bad)), HttpStatusCode.BadRequest);
            Assert.False(problem.TryGetProperty("type", out _));
            Assert.Equal(ErrorCodes.Malformed, problem.GetProperty("code").GetString());
        }

        Assert.False(PublicId<RecordIds>.TryParse(tampered, out _));
        Assert.False(PublicId<RecordIds>.TryParse(foreign, out _));
    }

    [Fact]
    public async Task An_unknown_but_valid_id_is_a_coded_not_found()
    {
        using var client = _host.NewClient();

        var response = await client.GetAsync(SampleRoutes.RecordFor(PublicId<RecordIds>.From(RecordStore.SeedRecordId + 1000).ToToken()));

        await Problems.AssertCodeAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }
}
