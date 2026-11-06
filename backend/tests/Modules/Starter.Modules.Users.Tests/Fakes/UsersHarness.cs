using Microsoft.Extensions.Time.Testing;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Application.Organizations;
using Starter.Modules.Users.Infrastructure.Import;

namespace Starter.Modules.Users.Tests.Fakes;

/// <summary>The module's use cases wired to the fakes, one set per test.</summary>
internal sealed class UsersHarness
{
    public const string LinkBase = "http://localhost:4280";

    public FakeAccounts Accounts { get; } = new();

    public FakeOutbox Outbox { get; } = new();

    public FakeNotifications Notifications { get; } = new();

    public RecordingLogger<AccountActivationEmailHandler> HandlerLog { get; } = new();

    public FakeOrganizations Organizations { get; } = new();

    public FakeBatches Batches { get; } = new();

    public FakeUnitOfWork UnitOfWork { get; } = new();

    public FakeCurrentUser CurrentUser { get; } = new();

    public FakeOrganizationScope Scope { get; } = new();

    public FakeAdministrationGuard Guard { get; } = new();

    public FakeLanguage Language { get; } = new();

    public FakeTimeProvider Clock { get; } = new(TestClock.Start);

    public ActivationLinkSettings Links { get; set; } = new(new Uri(LinkBase));

    public ImportSettings ImportSettings { get; set; } = new(MaxRows: 10, BatchLifetime: TimeSpan.FromMinutes(30));

    public AccountActivations Activations => new(Accounts, Outbox, new FakePublicIds(), Links);

    public UserViews Views => new(Organizations, Language);

    public AccountAccess Access => new(Accounts, Scope, Guard, CurrentUser, Organizations);

    public ScopedOrganizations Scoped => new(Organizations, Scope);

    public ImportRowValidator RowValidator => new(Organizations, Accounts, Scope);

    public ImportBatches ImportBatches => new(Batches, UnitOfWork, CurrentUser, ImportSettings, Clock);

    public CreateOrganization CreateOrganization() => new(new CreateOrganizationCommandValidator(), Organizations, Scoped, UnitOfWork, Clock);

    public UpdateOrganization UpdateOrganization() => new(new UpdateOrganizationCommandValidator(), Scoped, Accounts, UnitOfWork, Clock);

    public CreateUser CreateUser() => new(new CreateUserCommandValidator(), Accounts, Access, Activations, Views);

    public UpdateUser UpdateUser() => new(new UpdateUserCommandValidator(), Accounts, Access, Views);

    public ListUsers ListUsers() => new(Accounts, Access, Views);

    public GetUser GetUser() => new(Access, Views);

    public SetUserActive SetUserActive() => new(Accounts, Access);

    public UnlockUser UnlockUser() => new(Accounts, Access);

    public IssueUserActivation IssueUserActivation() => new(Access, Activations);

    public ListOrganizations ListOrganizations() => new(Organizations, Scoped);

    public GetOrganization GetOrganization() => new(Scoped);

    public SetOrganizationActive SetOrganizationActive() => new(Scoped, UnitOfWork, Clock);

    public PreviewUserImport Preview() => new(new XlsxSheetReader(), RowValidator, ImportBatches, ImportSettings);

    public CommitUserImport Commit() => new(ImportBatches, RowValidator, Accounts, Activations);

    public AccountActivationEmailHandler ActivationEmailHandler() => new(Accounts, Notifications, new FakePublicIds(), Links, HandlerLog);
}
