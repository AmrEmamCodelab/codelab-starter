using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Domain.Users;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure;
using Starter.Modules.Identity.Infrastructure.Notifications;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Notifications;

public sealed class NotificationRecipientsTests
{
    [Fact]
    public async Task An_account_with_an_email_is_reached_there_in_arabic_without_a_mobile()
    {
        var store = new InMemoryIdentityStore();
        var account = Register(store, "amal", email: "Amal@Example.test");

        var recipient = await new NotificationRecipients(store).FindAsync(account.Id, CancellationToken.None);

        Assert.Equal(new NotificationRecipient(account.Id, "Amal@Example.test", Mobile: null, AppLanguage.Arabic), recipient);
    }

    [Fact]
    public async Task An_account_without_an_email_has_no_address()
    {
        var store = new InMemoryIdentityStore();
        var account = Register(store, "badr", email: null);

        var recipient = await new NotificationRecipients(store).FindAsync(account.Id, CancellationToken.None);

        Assert.NotNull(recipient);
        Assert.Null(recipient.Email);
        Assert.Null(recipient.Mobile);
    }

    [Fact]
    public async Task A_configured_account_is_found_too()
    {
        var store = new InMemoryIdentityStore();
        var admin = store.AddAccount("admin", "a-long-password", isBreakGlass: true);

        var recipient = await new NotificationRecipients(store).FindAsync(admin.Id, CancellationToken.None);

        Assert.Equal(admin.Id, recipient!.UserId);
        Assert.Null(recipient.Email);
    }

    [Fact]
    public async Task An_unknown_user_is_null() =>
        Assert.Null(await new NotificationRecipients(new InMemoryIdentityStore()).FindAsync(404, CancellationToken.None));

    [Fact]
    public void The_default_language_is_arabic() =>
        Assert.Equal(AppLanguage.Arabic, NotificationRecipients.DefaultLanguage);

    [Fact]
    public void The_module_registers_the_recipient_directory()
    {
        var services = new ServiceCollection();

        services.AddIdentityModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(INotificationRecipients)
            && descriptor.ImplementationType == typeof(NotificationRecipients)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private static UserAccount Register(InMemoryIdentityStore store, string userName, string? email)
    {
        var account = UserAccount.Register(new AccountRegistration(
            Guid.NewGuid(),
            userName,
            new AccountProfile(userName + " display", email, LoginName: null, Upn: null, UserKind.External, AccountBinding.Password, OrganizationId: null),
            TestClock.Start));
        store.Add(account);
        store.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return account;
    }
}
