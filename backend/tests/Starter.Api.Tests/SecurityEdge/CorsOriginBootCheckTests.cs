using Codelab.Security.Cors;
using Starter.Infrastructure.Security;

namespace Starter.Api.Tests.SecurityEdge;

public sealed class CorsOriginBootCheckTests
{
    [Fact]
    public void Distinct_https_origins_are_valid_everywhere() =>
        Assert.Empty(CorsOriginBootCheck.Validate(Options("https://portal.example.test", "https://admin.example.test:8443"), isDevelopment: false));

    [Fact]
    public void No_origins_is_valid() => Assert.Empty(CorsOriginBootCheck.Validate(Options(), isDevelopment: false));

    [Fact]
    public void A_wildcard_is_refused_with_credentials_even_in_Development() =>
        Assert.Single(CorsOriginBootCheck.Validate(Options("*"), isDevelopment: true));

    [Fact]
    public void A_wildcard_without_credentials_is_refused_outside_Development()
    {
        var options = Options("*");
        options.AllowCredentials = false;

        Assert.Single(CorsOriginBootCheck.Validate(options, isDevelopment: false));
        Assert.Empty(CorsOriginBootCheck.Validate(options, isDevelopment: true));
    }

    [Fact]
    public void A_plain_http_origin_is_refused_outside_Development_only()
    {
        var options = Options("http://localhost:4280");

        Assert.Single(CorsOriginBootCheck.Validate(options, isDevelopment: false));
        Assert.Empty(CorsOriginBootCheck.Validate(options, isDevelopment: true));
    }

    [Theory]
    [InlineData("https://portal.example.test/")]
    [InlineData("https://portal.example.test/app")]
    [InlineData("https://portal.example.test?x=1")]
    [InlineData("https://portal.example.test:443")]
    [InlineData("portal.example.test")]
    [InlineData("")]
    public void Anything_but_a_bare_origin_is_refused(string origin) =>
        Assert.NotEmpty(CorsOriginBootCheck.Validate(Options(origin), isDevelopment: true));

    [Fact]
    public void An_origin_with_user_info_is_refused()
    {
        var problem = Assert.Single(CorsOriginBootCheck.Validate(Options("https://user@portal.example.test"), isDevelopment: false));

        Assert.Contains("user info", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicated_origin_is_refused_once_with_its_count()
    {
        var problem = Assert.Single(CorsOriginBootCheck.Validate(
            Options("https://portal.example.test", "https://admin.example.test", "https://portal.example.test", "https://portal.example.test"),
            isDevelopment: false));

        Assert.Contains("'https://portal.example.test' 3 times", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_host_on_another_scheme_or_port_is_not_a_duplicate() =>
        Assert.Empty(CorsOriginBootCheck.Validate(
            Options("http://localhost:4280", "https://localhost:4280", "http://localhost:4281"),
            isDevelopment: true));

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var problems = CorsOriginBootCheck.Validate(Options("http://portal.example.test", "https://a.example.test/", "https://b.example.test", "https://b.example.test"), isDevelopment: false);

        Assert.Equal(3, problems.Count);
    }

    private static CodelabCorsOptions Options(params string[] origins) => new() { AllowedOrigins = [.. origins] };
}
