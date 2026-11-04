using System.Net;
using System.Text.Json;
using Xunit;

namespace Package.Sample.Tests.Support;

/// <summary>Reads the one problem+json envelope the packages write for every error.</summary>
internal static class Problems
{
    public const string MediaType = "application/problem+json";

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(MediaType, response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        var problem = await ReadAsync(response, expectedStatus);
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }
}
