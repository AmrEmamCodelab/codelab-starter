using Codelab.Security.Tickets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure.LoginModes;

namespace Starter.Modules.Identity.Tests.LoginModes;

public sealed class AuthModesOptionsTests
{
    [Fact]
    public void Without_the_key_the_deployment_offers_basic_login_only()
    {
        var parsed = AuthModeNames.Parse(Bind([]).Modes);

        Assert.Equal([SignInMode.Basic], parsed.Modes);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void A_list_is_read_trimmed_case_insensitive_and_in_mode_order()
    {
        var options = Bind(new() { ["Auth:Modes:0"] = " SSO ", ["Auth:Modes:1"] = "basic", ["Auth:Modes:2"] = "Ad" });

        Assert.Equal([SignInMode.Basic, SignInMode.ActiveDirectory, SignInMode.Sso], AuthModeNames.Parse(options.Modes).Modes);
    }

    [Fact]
    public void A_scalar_value_is_a_comma_separated_list()
    {
        var options = Bind(new() { ["Auth:Modes"] = "ad, sso", ["Auth:WebBaseUrl"] = "https://portal.example.test" });

        Assert.Equal([SignInMode.ActiveDirectory, SignInMode.Sso], AuthModeNames.Parse(options.Modes).Modes);
        Assert.Equal("https://portal.example.test", options.WebBaseUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kerberos")]
    [InlineData("basic,basic")]
    [InlineData("basic,")]
    public void An_empty_unknown_or_repeated_mode_is_a_problem(string modes) =>
        Assert.NotEmpty(AuthModeNames.Parse(Bind(new() { ["Auth:Modes"] = modes }).Modes).Problems);

    [Fact]
    public void An_unknown_name_still_lets_the_known_ones_through_for_registration()
    {
        var parsed = AuthModeNames.Parse(["ad", "other"]);

        Assert.True(parsed.Contains(SignInMode.ActiveDirectory));
        Assert.Single(parsed.Problems);
    }

    [Fact]
    public void Basic_and_ad_need_nothing_else()
    {
        var result = Validate(new AuthModesOptions { Modes = ["basic", "ad"] }, tickets: Tickets(enabled: false));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Sso_needs_the_spa_base_url_and_sealed_tickets()
    {
        var result = Validate(new AuthModesOptions { Modes = ["sso"] }, tickets: Tickets(enabled: false));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Auth:WebBaseUrl", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains(SealedTicketOptions.SectionName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("portal.example.test")]
    [InlineData("ftp://portal.example.test")]
    [InlineData("https://portal.example.test/?a=1")]
    [InlineData("https://portal.example.test/#x")]
    public void Sso_refuses_a_spa_base_url_that_is_not_a_plain_absolute_url(string webBaseUrl) =>
        Assert.True(Validate(new AuthModesOptions { Modes = ["sso"], WebBaseUrl = webBaseUrl }, Tickets(enabled: true)).Failed);

    [Fact]
    public void Sso_needs_https_outside_development_only()
    {
        var options = new AuthModesOptions { Modes = ["sso"], WebBaseUrl = "http://localhost:4280" };

        Assert.True(Validate(options, Tickets(enabled: true), Environments.Development).Succeeded);
        Assert.True(Validate(options, Tickets(enabled: true), Environments.Production).Failed);
    }

    [Fact]
    public void Sso_needs_a_ticket_lifetime_that_fits_the_pending_sign_in()
    {
        var tickets = Tickets(enabled: true);
        tickets.MaxLifetimeSeconds = 60;

        Assert.True(Validate(new AuthModesOptions { Modes = ["sso"], WebBaseUrl = "https://portal.example.test" }, tickets).Failed);
    }

    [Fact]
    public void Validation_messages_name_keys_never_values()
    {
        var result = Validate(new AuthModesOptions { Modes = ["secret-looking-mode"] }, Tickets(enabled: false));

        Assert.DoesNotContain(result.Failures!, failure => failure.Contains("secret-looking-mode", StringComparison.Ordinal));
    }

    [Fact]
    public void The_login_mode_set_answers_from_the_options()
    {
        var set = new LoginModeSet(Options.Create(new AuthModesOptions { Modes = ["sso", "ad"] }));

        Assert.Equal([SignInMode.ActiveDirectory, SignInMode.Sso], set.Enabled);
        Assert.False(set.IsEnabled(SignInMode.Basic));
    }

    [Theory]
    [InlineData("https://portal.example.test", "https://portal.example.test/auth/sso/callback")]
    [InlineData("https://portal.example.test/app/", "https://portal.example.test/app/auth/sso/callback")]
    public void The_spa_callback_url_is_the_base_plus_the_callback_route(string webBaseUrl, string expected) =>
        Assert.Equal(expected, new ConfiguredSsoSpaCallback(Options.Create(new AuthModesOptions { WebBaseUrl = webBaseUrl })).CallbackUrl.AbsoluteUri);

    [Fact]
    public void The_spa_callback_url_needs_the_base() =>
        Assert.Throws<InvalidOperationException>(() => new ConfiguredSsoSpaCallback(Options.Create(new AuthModesOptions())).CallbackUrl);

    internal static SealedTicketOptions Tickets(bool enabled) => new() { Enabled = enabled };

    private static AuthModesOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new AuthModesOptions();
        AuthModesOptions.Bind(configuration.GetSection(AuthModesOptions.SectionName), options);
        return options;
    }

    private static ValidateOptionsResult Validate(AuthModesOptions options, SealedTicketOptions tickets, string environment = "Development") =>
        new AuthModesOptionsValidator(new TestHostEnvironment(environment), Options.Create(tickets)).Validate(name: null, options);
}
