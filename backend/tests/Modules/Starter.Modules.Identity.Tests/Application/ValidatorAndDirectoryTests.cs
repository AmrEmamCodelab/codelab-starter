using Starter.Application.Validation;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Application;

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void A_complete_command_is_valid() =>
        Assert.True(_validator.Validate(new LoginCommand("alice", "pw")).IsValid);

    [Fact]
    public void Blank_fields_are_required()
    {
        var errors = _validator.Validate(new LoginCommand(" ", null)).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["userName"]);
        Assert.Equal([ValidationMessages.Required], errors["password"]);
    }

    [Fact]
    public void Overlong_fields_are_refused()
    {
        var errors = _validator.Validate(new LoginCommand(
            new string('a', IdentityLimits.UserNameMaxLength + 1),
            new string('p', IdentityLimits.PasswordMaxLength + 1))).ToDictionary();

        Assert.Equal([ValidationMessages.TooLong], errors["userName"]);
        Assert.Equal([ValidationMessages.TooLong], errors["password"]);
    }
}

public sealed class RefreshCommandValidatorTests
{
    private readonly RefreshCommandValidator _validator = new();

    [Fact]
    public void A_token_is_required() =>
        Assert.Equal([ValidationMessages.Required], _validator.Validate(new RefreshCommand(null)).ToDictionary()["refreshToken"]);

    [Fact]
    public void An_overlong_token_is_refused() =>
        Assert.Equal(
            [ValidationMessages.TooLong],
            _validator.Validate(new RefreshCommand(new string('a', IdentityLimits.RefreshTokenMaxLength + 1))).ToDictionary()["refreshToken"]);

    [Fact]
    public void A_present_token_is_valid() =>
        Assert.True(_validator.Validate(new RefreshCommand("x")).IsValid);
}

public sealed class UserDirectoryTests
{
    [Fact]
    public async Task The_break_glass_account_holds_every_permission()
    {
        var store = new InMemoryIdentityStore();
        var admin = store.AddAccount("admin", "pw", isBreakGlass: true);

        var user = await new UserDirectory(store).FindBySubjectAsync(admin.SubjectId, CancellationToken.None);

        Assert.Equal(new Starter.Application.Users.DirectoryUser(UserId: admin.Id, IsActive: true, HoldsAllPermissions: true, DisplayName: admin.DisplayName), user);
        Assert.False(string.IsNullOrWhiteSpace(user!.DisplayName)); // the name other modules show, never the subject id
    }

    [Fact]
    public async Task An_ordinary_or_deactivated_account_holds_nothing_by_itself()
    {
        var store = new InMemoryIdentityStore();
        var alice = store.AddAccount("alice", "pw");
        var admin = store.AddAccount("admin", "pw", isBreakGlass: true);
        admin.Deactivate();
        var directory = new UserDirectory(store);

        var ordinary = await directory.FindBySubjectAsync(alice.SubjectId, CancellationToken.None);
        var deactivated = await directory.FindBySubjectAsync(admin.SubjectId, CancellationToken.None);

        Assert.False(ordinary!.HoldsAllPermissions);
        Assert.False(deactivated!.IsActive);
        Assert.False(deactivated.HoldsAllPermissions);
    }

    [Fact]
    public async Task An_unknown_subject_is_null() =>
        Assert.Null(await new UserDirectory(new InMemoryIdentityStore()).FindBySubjectAsync(Guid.NewGuid(), CancellationToken.None));
}
