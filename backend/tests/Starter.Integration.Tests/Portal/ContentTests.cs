using System.Net;
using System.Text.Json;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Portal;

/// <summary>
/// Portal content on real SQL: the administrator replaces the FAQ sections, and the anonymous read (cached on the node)
/// returns the new sections with their Arabic and English texts at once, because the replace drops the cached page.
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed class ContentTests(Phase5Host host)
{
    private const string PublicFaq = "/api/portal/content/faq";
    private const string AdminFaq = "/api/admin/portal/content/faq";
    private const string Sections = "sections";
    private const int FirstSortOrder = 10;
    private const int SecondSortOrder = 20;

    [Fact]
    public async Task An_admin_replaces_the_FAQ_and_the_anonymous_page_shows_it_in_Arabic_and_English()
    {
        using var api = host.CreateApi();
        var adminToken = await host.AdminTokenAsync();
        using (var warm = await api.GetAsync(PublicFaq, accessToken: null))
        {
            Assert.NotEmpty((await ApiClient.ReadAsync(warm, HttpStatusCode.OK)).GetProperty(Sections).EnumerateArray()); // seeded, now cached
        }

        var marker = TestNames.Suffix();
        var replacement = new[]
        {
            new SectionText($"سؤال أول {marker}", $"First question {marker}", $"الجواب الأول {marker}", $"First answer {marker}", FirstSortOrder),
            new SectionText($"سؤال ثان {marker}", $"Second question {marker}", $"الجواب الثاني {marker}", $"Second answer {marker}", SecondSortOrder),
        };

        using (var replaced = await api.PutAsync(AdminFaq, new { sections = replacement.Select(section => section.ToWire()).ToArray() }, adminToken))
        {
            var stored = await ApiClient.ReadAsync(replaced, HttpStatusCode.OK);
            Assert.Equal(replacement, Read(stored));
        }

        using var read = await api.GetAsync(PublicFaq, accessToken: null);
        var page = await ApiClient.ReadAsync(read, HttpStatusCode.OK);

        Assert.Equal(replacement, Read(page));
        Assert.Equal(JsonValueKind.String, page.GetProperty("updatedAt").ValueKind); // stamped by the edit
        Assert.All(page.GetProperty(Sections).EnumerateArray(), section => Assert.False(string.IsNullOrEmpty(section.GetProperty("id").GetString())));
    }

    private static List<SectionText> Read(JsonElement page) =>
        page.GetProperty(Sections).EnumerateArray()
            .Select(section => new SectionText(
                section.GetProperty("titleAr").GetString()!,
                section.GetProperty("titleEn").GetString()!,
                section.GetProperty("bodyAr").GetString()!,
                section.GetProperty("bodyEn").GetString()!,
                section.GetProperty("sortOrder").GetInt32()))
            .OrderBy(section => section.SortOrder)
            .ToList();

    private sealed record SectionText(string TitleAr, string TitleEn, string BodyAr, string BodyEn, int SortOrder)
    {
        public object ToWire() => new { id = (string?)null, titleAr = TitleAr, titleEn = TitleEn, bodyAr = BodyAr, bodyEn = BodyEn, sortOrder = SortOrder };
    }
}
