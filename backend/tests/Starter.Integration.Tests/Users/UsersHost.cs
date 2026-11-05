using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security;
using System.Text;
using System.Text.Json;
using Codelab.Security.Keys;
using Codelab.Security.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Integration.Tests.Support;
using Starter.Modules.Users.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Users;

/// <summary>
/// The Development host for the Users tests, with two test-only overrides the shared host settings do not carry: the
/// activation link base, and sealed tickets switched on with a fresh random key ring (activation links). Its own
/// throwaway database, created and dropped like every <see cref="IntegrationHost"/>.
/// </summary>
public sealed class UsersHost() : IntegrationHost(Environments.Development)
{
    /// <summary>The web origin the activation links point at.</summary>
    public const string LinkBase = "http://localhost:4280";

    /// <inheritdoc />
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.PostConfigure<ActivationOptions>(options => options.LinkBaseUrl = LinkBase);
        var keyId = StarterTestAccounts.NewKeyId();
        services.PostConfigure<SealedTicketOptions>(options =>
        {
            options.Enabled = true;
            options.ActiveKeyId = keyId;
            options.Keys.Clear();
            options.Keys.Add(new KeyEntryOptions { KeyId = keyId, MaterialBase64 = StarterTestAccounts.NewKeyBase64() });
        });
    }
}

/// <summary>Tests sharing the Users host.</summary>
[CollectionDefinition(Name)]
public sealed class UsersHostGroup : ICollectionFixture<UsersHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "users host";
}

/// <summary>The Users API as the contract states it (paths and JSON names only; no module types on the wire).</summary>
internal sealed class UsersApi(HttpClient http) : IDisposable
{
    public const string Organizations = "/api/admin/organizations";
    public const string Users = "/api/admin/users";
    public const string ImportPreview = "/api/admin/user-import/preview";
    public const string Activate = "/api/auth/activate";
    public const string ActivationFragment = "#ticket=";
    public const int ExternalKind = 2;
    public const int PasswordBinding = 1;

    private const string BearerScheme = "Bearer";
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static string Commit(string batchId) => $"/api/admin/user-import/{batchId}/commit";

    public static string UserPath(string id) => $"{Users}/{id}";

    public static string Deactivate(string id) => $"{Users}/{id}/deactivate";

    public Task<HttpResponseMessage> PostJsonAsync(string path, object body, string? accessToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) }, accessToken);

    public Task<HttpResponseMessage> PostAsync(string path, string accessToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)), accessToken);

    public Task<HttpResponseMessage> GetAsync(string path, string accessToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative)), accessToken);

    public Task<HttpResponseMessage> PostWorkbookAsync(byte[] workbook, string accessToken)
    {
        var file = new ByteArrayContent(workbook);
        file.Headers.ContentType = new MediaTypeHeaderValue(XlsxContentType);
        var form = new MultipartFormDataContent { { file, "file", "users.xlsx" } };
        return SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(ImportPreview, UriKind.Relative)) { Content = form }, accessToken);
    }

    /// <summary>Creates an external organization and returns its id (fails the test unless 201).</summary>
    public async Task<string> CreateExternalOrganizationAsync(string code, string accessToken)
    {
        using var response = await PostJsonAsync(Organizations, new { code, nameEn = code + " (en)", nameAr = code + " (ar)", kind = ExternalKind }, accessToken);
        return (await ReadAsync(response, System.Net.HttpStatusCode.Created)).GetProperty("id").GetString()!;
    }

    /// <summary>The body of a response with <paramref name="expected"/> status (fails the test otherwise).</summary>
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The sealed ticket carried in an activation link's fragment.</summary>
    public static string TicketOf(string activationLink)
    {
        var at = activationLink.IndexOf(ActivationFragment, StringComparison.Ordinal);
        Assert.True(at > 0, $"Not an activation link: {activationLink}");
        return Uri.UnescapeDataString(activationLink[(at + ActivationFragment.Length)..]);
    }

    public void Dispose() => http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string? accessToken)
    {
        using (request)
        {
            if (accessToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
            }

            return await http.SendAsync(request);
        }
    }
}

/// <summary>A minimal one-sheet .xlsx with inline strings, built in memory (the same layout the devseed sample writes).</summary>
internal static class TestWorkbook
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Build(params string?[][] rows)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "_rels/.rels", $"<Relationships xmlns=\"{Relationships}\"><Relationship Id=\"rId1\" Type=\"{OfficeRelationships}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Write(zip, "xl/workbook.xml", $"<workbook xmlns=\"{Main}\" xmlns:r=\"{OfficeRelationships}\"><sheets><sheet name=\"Users\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Write(zip, "xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"{Relationships}\"><Relationship Id=\"rId1\" Type=\"{OfficeRelationships}/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Write(zip, "xl/worksheets/sheet1.xml", Sheet(rows));
        }

        return buffer.ToArray();
    }

    private static string Sheet(string?[][] rows)
    {
        var xml = new StringBuilder($"<worksheet xmlns=\"{Main}\"><sheetData>");
        for (var index = 0; index < rows.Length; index++)
        {
            var number = (index + 1).ToString(CultureInfo.InvariantCulture);
            xml.Append("<row r=\"").Append(number).Append("\">");
            for (var column = 0; column < rows[index].Length; column++)
            {
                if (rows[index][column] is { } text)
                {
                    xml.Append("<c r=\"").Append((char)('A' + column)).Append(number).Append("\" t=\"inlineStr\"><is><t>")
                        .Append(SecurityElement.Escape(text)).Append("</t></is></c>");
                }
            }

            xml.Append("</row>");
        }

        return xml.Append("</sheetData></worksheet>").ToString();
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        using var stream = zip.CreateEntry(path).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
