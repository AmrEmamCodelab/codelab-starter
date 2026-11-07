using Codelab.Observability;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.Replay;
using Codelab.Security.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Auditing;
using Starter.Modules.Identity.Application.Activation;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure.Accounts;
using Starter.Modules.Identity.Infrastructure.Auditing;
using Starter.Modules.Identity.Infrastructure.Options;
using Starter.Modules.Identity.Infrastructure.Persistence;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Infrastructure;

public sealed class AccountSecurityOptionsTests
{
    [Fact]
    public void The_defaults_are_valid_and_turn_into_the_policies()
    {
        Assert.True(new LockoutOptionsValidator().Validate(name: null, new LockoutOptions()).Succeeded);
        Assert.True(new PasswordPolicyOptionsValidator().Validate(name: null, new PasswordPolicyOptions()).Succeeded);
        Assert.True(new ActivationOptionsValidator().Validate(name: null, new ActivationOptions()).Succeeded);
        Assert.Equal(new LockoutPolicy(5, TimeSpan.FromMinutes(15)), new LockoutOptions().ToPolicy());
        Assert.Equal(new PasswordPolicy(12, 5), new PasswordPolicyOptions().ToPolicy());
        Assert.Equal(TimeSpan.FromHours(72), new ActivationOptions().TicketLifetime);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(5, 0)]
    [InlineData(101, 15)]
    [InlineData(5, 1441)]
    public void Lockout_values_out_of_range_fail_the_start(int attempts, int minutes) =>
        Assert.True(new LockoutOptionsValidator().Validate(name: null, new LockoutOptions { MaxFailedAttempts = attempts, LockoutMinutes = minutes }).Failed);

    [Theory]
    [InlineData(7, 5)]
    [InlineData(257, 5)]
    [InlineData(12, 0)]
    [InlineData(12, 13)]
    public void A_password_rule_below_the_floor_or_incoherent_fails_the_start(int minLength, int distinct) =>
        Assert.True(new PasswordPolicyOptionsValidator().Validate(name: null, new PasswordPolicyOptions { MinLength = minLength, MinDistinctCharacters = distinct }).Failed);

    [Theory]
    [InlineData(0)]
    [InlineData(169)]
    public void An_activation_lifetime_out_of_range_fails_the_start(int hours) =>
        Assert.True(new ActivationOptionsValidator().Validate(name: null, new ActivationOptions { TicketLifetimeHours = hours }).Failed);
}

public sealed class SealedActivationTicketsTests
{
    private readonly FakeTimeProvider _clock = new(TestClock.Start);

    [Fact]
    public async Task A_ticket_opens_once_for_its_subject_and_id()
    {
        var tickets = Create(enabled: true);
        var subject = Guid.NewGuid();

        var issued = tickets.Issue(subject);
        var opened = await tickets.OpenAsync(issued.Ticket, CancellationToken.None);
        var again = await tickets.OpenAsync(issued.Ticket, CancellationToken.None);

        Assert.Equal(new OpenedActivationTicket(subject, issued.TicketId), opened);
        Assert.Null(again);
        Assert.Equal(TestClock.Start + TimeSpan.FromHours(ActivationOptions.DefaultTicketLifetimeHours), issued.ExpiresAt);
        Assert.DoesNotContain(issued.TicketId, issued.Ticket, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.Ticket, issued.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_or_forged_ticket_does_not_open()
    {
        var tickets = Create(enabled: true);
        var issued = tickets.Issue(Guid.NewGuid());

        Assert.Null(await tickets.OpenAsync("t1.k1.forged", CancellationToken.None));
        _clock.Advance(TimeSpan.FromHours(ActivationOptions.DefaultTicketLifetimeHours));
        Assert.Null(await tickets.OpenAsync(issued.Ticket, CancellationToken.None));
    }

    [Fact]
    public void The_lifetime_is_capped_by_the_ticket_maximum()
    {
        var tickets = Create(enabled: true, maxLifetimeSeconds: 3600);

        Assert.Equal(TestClock.Start.AddHours(1), tickets.Issue(Guid.NewGuid()).ExpiresAt);
    }

    [Fact]
    public async Task Disabled_tickets_are_unavailable_and_never_open()
    {
        var tickets = Create(enabled: false);

        Assert.False(tickets.IsAvailable);
        Assert.Throws<InvalidOperationException>(() => tickets.Issue(Guid.NewGuid()));
        Assert.Null(await tickets.OpenAsync("t1.k1.anything", CancellationToken.None));
    }

    private SealedActivationTickets Create(bool enabled, int maxLifetimeSeconds = 7 * 24 * 60 * 60)
    {
        var options = new SealedTicketOptions
        {
            Enabled = enabled,
            MaxLifetimeSeconds = maxLifetimeSeconds,
            ActiveKeyId = "k1",
            Keys = [new KeyEntryOptions { KeyId = "k1", MaterialBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) }],
        };
        var services = new ServiceCollection()
            .AddSingleton<ISealedTicketService>(new SealedTicketService(options, new MemoryReplayGuard(_clock, entryLimit: 100), _clock, SecurityProtocol.Create("CODELABTEST")))
            .BuildServiceProvider();
        return new SealedActivationTickets(Options.Create(options), Options.Create(new ActivationOptions()), services, _clock);
    }
}

public sealed class EfAuthEventSinkTests
{
    [Fact]
    public async Task A_failed_write_is_logged_and_never_fails_the_caller_and_the_typed_name_is_only_hashed()
    {
        var logger = new CapturingLogger();
        var hash = UserIdHash.Create(new UserIdHashSettings(), new DevelopmentEnvironment());
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=tcp:127.0.0.1,1;Database=NeverOpened;Connect Timeout=1;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        var sink = new EfAuthEventSink(options, new FixedAuditContext(), hash, new FakeTimeProvider(TestClock.Start), logger);

        await sink.RecordAsync(
            new AuthEventRecord(AuthEventType.LoginFailed, UserName: "typed-name", Mode: SignInMode.Basic, Reason: AuthFailureReason.UnknownAccount),
            CancellationToken.None);

        Assert.Equal([LogLevel.Warning, LogLevel.Error], logger.Entries.Select(entry => entry.Level));
        Assert.Contains(hash.Of("typed-name")!, logger.Entries[0].Message, StringComparison.Ordinal);
        Assert.All(logger.Entries, entry => Assert.DoesNotContain("typed-name", entry.Message, StringComparison.Ordinal));
        Assert.Contains("correlation-1", logger.Entries[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_record_never_prints_the_typed_name() =>
        Assert.DoesNotContain("typed-name", new AuthEventRecord(AuthEventType.LoginFailed, UserName: "typed-name").ToString(), StringComparison.Ordinal);

    private sealed class FixedAuditContext : IAuditContextAccessor
    {
        public AuditContext Current { get; } = new(SubjectId: null, ClientAddress: null, CorrelationId: "correlation-1");
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class CapturingLogger : ILogger<EfAuthEventSink>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}

public sealed class InitialCreateMigrationTests
{
    [Fact]
    public void The_one_migration_carries_every_operation_of_the_model_diff()
    {
        using var context = NewContext();
        var migration = new Starter.Modules.Identity.Infrastructure.Persistence.Migrations.InitialCreate();
        var target = Initialize(context, migration.TargetModel!);

        var expected = context.GetService<IMigrationsModelDiffer>().GetDifferences(source: null, target.GetRelationalModel());

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(Describe).Order(StringComparer.Ordinal), migration.UpOperations.Select(Describe).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_fresh_database_gets_nullable_password_hashes_unique_when_present_names_and_the_auth_event_trail()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains($"[PasswordHash] nvarchar({IdentityLimits.PasswordHashMaxLength}) NULL", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [identity].[AuthEvents]", script, StringComparison.Ordinal);
        Assert.Contains("WHERE [NormalizedEmail] IS NOT NULL", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER COLUMN", script, StringComparison.Ordinal);
    }

    private static IdentityDbContext NewContext() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, IdentityDbContext.SchemaName))
        .Options);

    private static IModel Initialize(IdentityDbContext context, IModel model) =>
        context.GetService<IModelRuntimeInitializer>().Initialize(model, designTime: true);

    private static string Describe(MigrationOperation operation) => operation switch
    {
        AddColumnOperation add =>
            $"add {add.Table}.{add.Name} {add.ColumnType} null={add.IsNullable}",
        AlterColumnOperation alter =>
            $"alter {alter.Table}.{alter.Name} {alter.ColumnType} null={alter.IsNullable}",
        CreateIndexOperation index =>
            $"index {index.Table}.{index.Name} ({string.Join(",", index.Columns)}) unique={index.IsUnique} filter={index.Filter}",
        CreateTableOperation table =>
            $"table {table.Name} ({string.Join(",", table.Columns.OrderBy(column => column.Name, StringComparer.Ordinal).Select(column => $"{column.Name}:{column.ColumnType}:{column.IsNullable}"))}) "
            + $"fk=({string.Join(",", table.ForeignKeys.OrderBy(key => key.Name, StringComparer.Ordinal).Select(key => $"{key.Name}:{key.OnDelete}"))})",
        EnsureSchemaOperation ensure => $"schema {ensure.Name}",
        _ => operation.GetType().Name,
    };
}
