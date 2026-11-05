using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Tests.TestSupport;

namespace Codelab.Identity.Tests.ActiveDirectory;

public sealed class LdapFailureMappingTests
{
    [Theory]
    [InlineData("52e", DirectoryAuthStatus.InvalidCredentials)]
    [InlineData("525", DirectoryAuthStatus.InvalidCredentials)]
    [InlineData("532", DirectoryAuthStatus.InvalidCredentials)]
    [InlineData("533", DirectoryAuthStatus.AccountDisabled)]
    [InlineData("701", DirectoryAuthStatus.AccountDisabled)]
    [InlineData("775", DirectoryAuthStatus.AccountDisabled)]
    public void Invalid_credentials_is_split_by_the_directory_sub_code(string subCode, DirectoryAuthStatus expected) =>
        Assert.Equal(expected, LdapFailureMapping.ForBind(FakeDirectory.InvalidCredentials(subCode)));

    [Fact]
    public void Invalid_credentials_without_a_sub_code_is_invalid_credentials() =>
        Assert.Equal(DirectoryAuthStatus.InvalidCredentials, LdapFailureMapping.ForBind(FakeDirectory.Code(LdapResultCodes.InvalidCredentials)));

    [Theory]
    [InlineData(LdapResultCodes.TimeLimitExceeded)]
    [InlineData(LdapResultCodes.StrongerAuthRequired)]
    [InlineData(LdapResultCodes.Busy)]
    [InlineData(LdapResultCodes.Unavailable)]
    [InlineData(LdapResultCodes.ServerDown)]
    [InlineData(LdapResultCodes.Timeout)]
    [InlineData(LdapResultCodes.ConnectError)]
    [InlineData(null)]
    public void Every_other_code_is_unavailable(int? resultCode) =>
        Assert.Equal(DirectoryAuthStatus.Unavailable, LdapFailureMapping.ForBind(FakeDirectory.Code(resultCode)));

    [Theory]
    [InlineData("comment: AcceptSecurityContext error, data 775, v4563", "775")]
    [InlineData("DATA 52E,", "52E")]
    [InlineData("no sub-code here", null)]
    [InlineData("data ,", null)]
    [InlineData(null, null)]
    public void The_sub_code_is_read_after_the_marker(string? message, string? expected) =>
        Assert.Equal(expected, LdapFailureMapping.SubCodeOf(message));

    [Theory]
    [InlineData("512", false)]
    [InlineData("514", true)]
    [InlineData("2", true)]
    [InlineData("not-a-number", false)]
    [InlineData(null, false)]
    public void The_account_disabled_flag_is_read_from_user_account_control(string? flags, bool disabled) =>
        Assert.Equal(disabled, LdapFailureMapping.IsDisabled(flags));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a*b", "a\\2ab")]
    [InlineData("(x)", "\\28x\\29")]
    [InlineData("back\\slash", "back\\5cslash")]
    [InlineData("nul\u0000", "nul\\00")]
    public void Filter_values_are_escaped_per_rfc_4515(string value, string expected) =>
        Assert.Equal(expected, LdapFilter.Escape(value));

    [Fact]
    public void Enum_members_keep_their_stored_values()
    {
        Assert.Equal([1, 2, 3, 4], Enum.GetValues<DirectoryAuthStatus>().Select(value => (int)value));
        Assert.Equal([1, 2, 3, 4], Enum.GetValues<DirectoryProbeStatus>().Select(value => (int)value));
        Assert.Equal([1, 2, 3], Enum.GetValues<LoginMatchField>().Select(value => (int)value));
    }
}
