using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Tests.LoginModes;

public sealed class ReturnUrlPolicyTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/admin")]
    [InlineData("/admin/users?page=2&search=a%20b")]
    [InlineData("/requests/7#details")]
    public void A_root_relative_path_is_kept(string returnUrl) => Assert.Equal(returnUrl, ReturnUrlPolicy.Sanitize(returnUrl));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("admin")]
    [InlineData("https://evil.example.test/")]
    [InlineData("//evil.example.test")]
    [InlineData("/\\evil.example.test")]
    [InlineData("/admin\\..\\x")]
    [InlineData("/a\tb")]
    [InlineData("/a\nb")]
    [InlineData("javascript:alert(1)")]
    public void Anything_else_becomes_the_root(string? returnUrl) => Assert.Equal(ReturnUrlPolicy.Root, ReturnUrlPolicy.Sanitize(returnUrl));

    [Fact]
    public void An_over_long_path_becomes_the_root()
    {
        var longPath = "/" + new string('a', SsoLimits.ReturnUrlMaxLength);

        Assert.Equal(ReturnUrlPolicy.Root, ReturnUrlPolicy.Sanitize(longPath));
        Assert.Equal(longPath[..SsoLimits.ReturnUrlMaxLength], ReturnUrlPolicy.Sanitize(longPath[..SsoLimits.ReturnUrlMaxLength]));
    }
}

public sealed class SsoBindingTests
{
    private const string Proof = "a-binding-secret-of-at-least-32-characters";

    [Fact]
    public void A_hash_of_a_proof_is_shaped_like_one_and_accepts_only_that_proof()
    {
        var hash = SsoBinding.HashOf(Proof);

        Assert.True(SsoBinding.IsHashShaped(hash));
        Assert.True(SsoBinding.Accepts(bindingHash: hash, proof: Proof));
        Assert.False(SsoBinding.Accepts(bindingHash: hash, proof: Proof + "x"));
        Assert.False(SsoBinding.Accepts(bindingHash: hash, proof: null));
        Assert.False(SsoBinding.Accepts(bindingHash: hash, proof: string.Empty));
    }

    [Fact]
    public void An_unbound_ticket_is_never_accepted()
    {
        Assert.False(SsoBinding.Accepts(bindingHash: null, proof: null));
        Assert.False(SsoBinding.Accepts(bindingHash: null, proof: Proof));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tooshort")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void Other_values_are_not_hash_shaped(string? value) => Assert.False(SsoBinding.IsHashShaped(value));
}

public sealed class SsoSpaFragmentTests
{
    private static readonly Uri Callback = new(FakeSpaCallback.Url);

    [Fact]
    public void A_ticket_and_a_return_path_travel_in_the_fragment_only()
    {
        var location = SsoSpaFragment.Location(Callback, new SsoCallbackResult(Ticket: "t1.k.abc-_", ErrorCode: null, ReturnUrl: "/admin?x=1"));

        Assert.Equal(string.Empty, location.Query);
        Assert.Equal("#ticket=t1.k.abc-_&returnUrl=%2Fadmin%3Fx%3D1", location.Fragment);
        Assert.Equal(Callback.AbsolutePath, location.AbsolutePath);
    }

    [Fact]
    public void An_error_omits_the_root_return_path() =>
        Assert.Equal(
            "#error=" + LoginModeErrorCodes.SsoUnavailable,
            SsoSpaFragment.Location(Callback, SsoCallbackResult.Failed(errorCode: LoginModeErrorCodes.SsoUnavailable, returnUrl: ReturnUrlPolicy.Root)).Fragment);
}

public sealed class LoginModesQueryTests
{
    [Fact]
    public void The_query_reports_exactly_the_enabled_modes() =>
        Assert.Equal(
            new EnabledLoginModes(Basic: false, ActiveDirectory: true, Sso: true),
            new LoginModesQuery(new FakeLoginModes(SignInMode.ActiveDirectory, SignInMode.Sso)).Execute());
}
