using System.Net;
using System.Text.Json;
using Starter.Domain.Lookups;
using Starter.Integration.Tests.Support;
using Starter.Modules.Lookups.Application;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Lookups;

/// <summary>
/// The lookups API on real SQL: the seeded contact topics are read by a signed-in user (active values only); the read
/// is cached, so a change made behind the API stays invisible until an administrator's deactivation through the API
/// drops the cached list, after which both changes show.
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed class LookupsApiTests(Phase5Host host)
{
    private const string ActiveTopics = "/api/lookups/" + LookupTypes.ContactTopics;
    private const string AdminTopics = "/api/admin/lookups/" + LookupTypes.ContactTopics;
    private const string AdminTypes = "/api/admin/lookups";
    private const string UnknownType = "/api/lookups/no-such-type";
    private const string CodeMember = "code";
    private const int SeededTopicCount = 4;
    private const int DeactivatedByTheTest = 2;

    private const string CodeOf = "SELECT [Code] FROM [lookups].[LookupItems] WHERE [TypeKey] = @type AND [Id] = @id";
    private const string SetActive = "UPDATE [lookups].[LookupItems] SET [IsActive] = @active WHERE [TypeKey] = @type AND [Id] = @id";

    [Fact]
    public async Task Reads_are_cached_until_a_deactivation_through_the_API_drops_the_cached_list()
    {
        using var api = host.CreateApi();
        var adminToken = await host.AdminTokenAsync();
        var behindTheApi = await CodeOfAsync(ContactTopicIds.Suggestion);
        var throughTheApi = await CodeOfAsync(ContactTopicIds.Account);
        try
        {
            Assert.Equal(SeededTopicCount, (await ActiveCodesAsync(api, adminToken)).Count); // read and cached

            await SetActiveInDatabaseAsync(ContactTopicIds.Suggestion, active: false);
            Assert.Contains(behindTheApi, await ActiveCodesAsync(api, adminToken)); // still the cached list

            await SetActiveThroughApiAsync(api, throughTheApi, active: false, adminToken);
            var after = await ActiveCodesAsync(api, adminToken);

            Assert.Equal(SeededTopicCount - DeactivatedByTheTest, after.Count);
            Assert.DoesNotContain(behindTheApi, after);
            Assert.DoesNotContain(throughTheApi, after);
            Assert.Contains(await AdminItemsAsync(api, adminToken), item => item.GetProperty(CodeMember).GetString() == throughTheApi && !item.GetProperty("isActive").GetBoolean());
        }
        finally
        {
            await SetActiveInDatabaseAsync(ContactTopicIds.Suggestion, active: true);
            await SetActiveThroughApiAsync(api, throughTheApi, active: true, adminToken); // restores and drops the cache
        }

        Assert.Equal(SeededTopicCount, (await ActiveCodesAsync(api, adminToken)).Count);
    }

    [Fact]
    public async Task The_admin_list_counts_the_seeded_type_and_an_unknown_type_is_404_problem_json()
    {
        using var api = host.CreateApi();
        var adminToken = await host.AdminTokenAsync();

        using var types = await api.GetAsync(AdminTypes, adminToken);
        using var unknown = await api.GetAsync(UnknownType, adminToken);
        using var anonymous = await api.GetAsync(ActiveTopics, accessToken: null);

        var contactTopics = Assert.Single(
            (await ApiClient.ReadAsync(types, HttpStatusCode.OK)).EnumerateArray(),
            type => type.GetProperty("type").GetString() == LookupTypes.ContactTopics);
        Assert.Equal(SeededTopicCount, contactTopics.GetProperty("itemCount").GetInt32());
        Assert.Equal(LookupsErrorCodes.TypeNotFound, await StarterProblems.AssertProblemAsync(unknown, HttpStatusCode.NotFound));
        await StarterProblems.AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized);
    }

    private static async Task<IReadOnlyList<string>> ActiveCodesAsync(ApiClient api, string accessToken)
    {
        using var response = await api.GetAsync(ActiveTopics, accessToken);
        var items = await ApiClient.ReadAsync(response, HttpStatusCode.OK);
        Assert.All(items.EnumerateArray(), item => Assert.False(string.IsNullOrEmpty(item.GetProperty("nameAr").GetString())));
        return items.EnumerateArray().Select(item => item.GetProperty(CodeMember).GetString()!).ToList();
    }

    private static async Task<IReadOnlyList<JsonElement>> AdminItemsAsync(ApiClient api, string accessToken)
    {
        using var response = await api.GetAsync(AdminTopics, accessToken);
        return (await ApiClient.ReadAsync(response, HttpStatusCode.OK)).EnumerateArray().ToList();
    }

    private static async Task SetActiveThroughApiAsync(ApiClient api, string code, bool active, string adminToken)
    {
        var id = (await AdminItemsAsync(api, adminToken)).Single(item => item.GetProperty(CodeMember).GetString() == code).GetProperty("id").GetString();
        using var response = await api.PutAsync($"{AdminTopics}/items/{id}/active", new { isActive = active }, adminToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<string> CodeOfAsync(int id) =>
        Assert.IsType<string>(await TestSqlServer.ScalarAsync(host.DatabaseName, CodeOf, Item(id)));

    private async Task SetActiveInDatabaseAsync(int id, bool active)
    {
        var parameters = new Dictionary<string, object>(Item(id)) { ["@active"] = active };
        Assert.Equal(1, await TestSqlServer.ExecuteAsync(host.DatabaseName, SetActive, parameters));
    }

    private static Dictionary<string, object> Item(int id) => new() { ["@type"] = LookupTypes.ContactTopics, ["@id"] = id };
}
