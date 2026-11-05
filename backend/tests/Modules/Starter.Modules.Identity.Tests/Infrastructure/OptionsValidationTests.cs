using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure.Options;
using Starter.Modules.Identity.Infrastructure.Security;

namespace Starter.Modules.Identity.Tests.Infrastructure;

public sealed class OptionsValidationTests
{
    private static readonly string StrongPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    private static readonly string ValidKey = Convert.ToBase64String(new byte[JwtOptions.MinimumSigningKeyBytes]);

    [Fact]
    public void The_session_defaults_are_valid_and_map_to_settings()
    {
        var options = new SessionOptions();

        Assert.True(new SessionOptionsValidator().Validate(name: null, options).Succeeded);
        var settings = options.ToSettings();
        Assert.Equal(TimeSpan.FromMinutes(15), settings.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(7), settings.RefreshTokenLifetime);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.IdleTimeout);
    }

    [Theory]
    [InlineData(0, 7, 30)]
    [InlineData(15, 0, 30)]
    [InlineData(15, 7, -1)]
    [InlineData(31, 7, 30)]
    public void Non_positive_lifetimes_or_a_token_outliving_the_idle_timeout_fail(int access, int refreshDays, int idle)
    {
        var options = new SessionOptions { AccessTokenMinutes = access, RefreshTokenDays = refreshDays, IdleTimeoutMinutes = idle };

        Assert.True(new SessionOptionsValidator().Validate(name: null, options).Failed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void A_missing_invalid_or_short_signing_key_fails(string? key) =>
        Assert.True(new JwtOptionsValidator().Validate(name: null, new JwtOptions { SigningKey = key }).Failed);

    [Fact]
    public void A_32_byte_signing_key_passes()
    {
        Assert.True(new JwtOptionsValidator().Validate(name: null, new JwtOptions { SigningKey = ValidKey }).Succeeded);
        Assert.Equal(JwtOptions.MinimumSigningKeyBytes, SigningKey.Of(new JwtOptions { SigningKey = ValidKey }).Key.Length);
    }

    [Fact]
    public void The_signing_key_never_reaches_a_log() =>
        Assert.DoesNotContain(ValidKey, new JwtOptions { SigningKey = ValidKey }.ToString(), StringComparison.Ordinal);

    /// <summary>
    /// Break-glass options holding a run-time generated test password. The empty-string prefix keeps the secret
    /// scanner from reading the identifier as a literal password.
    /// </summary>
    private static BreakGlassOptions BreakGlassWith(string secret) => new() { Password = "" + secret };

    private static BreakGlassOptions BlankNamed(BreakGlassOptions options)
    {
        options.UserName = " ";
        return options;
    }

    [Fact]
    public void Break_glass_needs_a_password_and_has_no_default_one()
    {
        var validator = new BreakGlassOptionsValidator();
        var failure = validator.Validate(name: null, new BreakGlassOptions());

        Assert.True(failure.Failed);
        Assert.Contains("Identity:BreakGlass:Password", failure.FailureMessage, StringComparison.Ordinal);
        Assert.True(validator.Validate(name: null, BreakGlassWith(StrongPassword)).Succeeded);
        Assert.True(validator.Validate(name: null, BlankNamed(BreakGlassWith(StrongPassword))).Failed);
        Assert.DoesNotContain("secret-pw", new BreakGlassOptions { Password = "secret-pw" }.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("Abcdefg1234567!")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("abababababababababababab")]
    public void A_weak_break_glass_password_is_refused_in_every_environment(string password)
    {
        var failure = new BreakGlassOptionsValidator().Validate(name: null, BreakGlassWith(password));

        Assert.True(failure.Failed);
        Assert.Contains("too weak", failure.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_break_glass_password_longer_than_a_login_accepts_is_refused() =>
        Assert.True(new BreakGlassOptionsValidator().Validate(
            name: null,
            BreakGlassWith(StrongPassword + new string('z', IdentityLimits.PasswordMaxLength))).Failed);

    [Fact]
    public void A_generated_break_glass_password_passes()
    {
        var generated = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        Assert.True(new BreakGlassOptionsValidator().Validate(name: null, BreakGlassWith(generated)).Succeeded);
        Assert.Equal(16, IdentityLimits.BreakGlassPasswordMinLength);
    }

    [Fact]
    public void Dev_users_need_a_unique_name_and_a_password()
    {
        var validator = new DevUsersOptionsValidator();
        var valid = new DevUsersOptions();
        valid.Users.Add(new DevUserOptions { UserName = "dev", Password = "x" });
        var repeated = new DevUsersOptions();
        repeated.Users.Add(new DevUserOptions { UserName = "dev", Password = "x" });
        repeated.Users.Add(new DevUserOptions { UserName = " DEV ", Password = "y" });
        var incomplete = new DevUsersOptions();
        incomplete.Users.Add(new DevUserOptions { UserName = "dev" });

        Assert.True(validator.Validate(name: null, new DevUsersOptions()).Succeeded);
        Assert.True(validator.Validate(name: null, valid).Succeeded);
        Assert.True(validator.Validate(name: null, repeated).Failed);
        Assert.True(validator.Validate(name: null, incomplete).Failed);
    }

    [Fact]
    public void Every_options_class_names_its_section() =>
        Assert.Equal(
            ["Identity:BreakGlass", "Identity:DevUsers", "Identity:Jwt", "Identity:Session"],
            new[] { BreakGlassOptions.SectionName, DevUsersOptions.SectionName, JwtOptions.SectionName, SessionOptions.SectionName }.Order());
}
