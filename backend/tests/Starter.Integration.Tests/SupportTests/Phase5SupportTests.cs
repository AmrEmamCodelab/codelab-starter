using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.SupportTests;

/// <summary>The clock, the service decoration, the recipient override and the probe handlers (no database).</summary>
public sealed class Phase5SupportTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private const long UserId = 42;

    [Fact]
    public void The_clock_runs_with_real_time_shifts_both_ways_and_resets()
    {
        var real = new FakeTimeProvider(Start);
        var clock = new ShiftableClock(real);

        clock.Advance(FiveMinutes);
        real.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(Start + FiveMinutes + TimeSpan.FromSeconds(1), clock.GetUtcNow());

        var past = Start.AddYears(-1);
        clock.TravelTo(past);
        Assert.Equal(past, clock.GetUtcNow());
        clock.Reset();
        Assert.Equal(real.GetUtcNow(), clock.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, clock.Offset);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-FiveMinutes));
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void Decorate_wraps_a_type_registration_and_keeps_its_lifetime(ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Describe(typeof(INotificationRecipients), typeof(StubRecipients), lifetime));
        var overrides = new RecipientOverrides();

        services.Decorate<INotificationRecipients>((_, inner) => new OverridingRecipients(inner, overrides));

        var descriptor = Assert.Single(services);
        Assert.Equal(lifetime, descriptor.Lifetime);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<OverridingRecipients>(scope.ServiceProvider.GetRequiredService<INotificationRecipients>());
    }

    [Fact]
    public void Decorate_also_wraps_an_instance_or_a_factory_and_refuses_a_missing_service()
    {
        var instance = new ServiceCollection().AddSingleton<INotificationRecipients>(new StubRecipients());
        var factory = new ServiceCollection().AddScoped<INotificationRecipients>(_ => new StubRecipients());
        var overrides = new RecipientOverrides();

        instance.Decorate<INotificationRecipients>((_, inner) => new OverridingRecipients(inner, overrides));
        factory.Decorate<INotificationRecipients>((_, inner) => new OverridingRecipients(inner, overrides));

        using var fromInstance = instance.BuildServiceProvider();
        using var fromFactory = factory.BuildServiceProvider();
        using var scope = fromFactory.CreateScope();
        Assert.IsType<OverridingRecipients>(fromInstance.GetRequiredService<INotificationRecipients>());
        Assert.IsType<OverridingRecipients>(scope.ServiceProvider.GetRequiredService<INotificationRecipients>());
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().Decorate<INotificationRecipients>((_, inner) => inner));
    }

    [Fact]
    public async Task The_override_applies_to_known_users_only_while_in_force()
    {
        var overrides = new RecipientOverrides();
        var recipients = new OverridingRecipients(new StubRecipients(), overrides);

        Assert.Equal(StubRecipients.Known, await recipients.FindAsync(UserId, CancellationToken.None));
        using (overrides.Use(new RecipientOverride("x@example.test", "+9665", AppLanguage.English)))
        {
            Assert.Equal(new NotificationRecipient(UserId, "x@example.test", "+9665", AppLanguage.English), await recipients.FindAsync(UserId, CancellationToken.None));
            Assert.Null(await recipients.FindAsync(UserId + 1, CancellationToken.None));
        }

        Assert.Null(overrides.Current);
        Assert.Equal(StubRecipients.Known, await recipients.FindAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task The_idempotent_probe_applies_its_effect_once_per_message_id_and_the_failing_probe_always_throws()
    {
        var ledger = new ProbeLedger();
        var payload = new ProbePayload("p1").ToJson();
        var idempotent = new IdempotentProbeHandler(ledger);
        var failing = new FailingProbeHandler(ledger);

        await idempotent.HandleAsync(new OutboxDelivery(MessageId: 7, ProbeMessageTypes.Idempotent, payload, Attempt: 1), CancellationToken.None);
        await idempotent.HandleAsync(new OutboxDelivery(MessageId: 7, ProbeMessageTypes.Idempotent, payload, Attempt: 2), CancellationToken.None);
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.HandleAsync(new OutboxDelivery(MessageId: 8, ProbeMessageTypes.Failing, new ProbePayload("p2").ToJson(), Attempt: 1), CancellationToken.None));

        Assert.Equal(2, ledger.DeliveriesOf("p1").Count);
        Assert.Equal(1, ledger.EffectsOf("p1"));
        Assert.Single(ledger.DeliveriesOf("p2"));
        Assert.Equal(0, ledger.EffectsOf("p2"));
        Assert.Equal(FailingProbeHandler.FailureText, thrown.Message);
    }

    [Fact]
    public void Canaries_are_unique_labelled_values_and_the_email_uses_the_reserved_domain()
    {
        var first = PiiCanaries.Generate();
        var second = PiiCanaries.Generate();

        Assert.Equal(4, first.All.Count);
        Assert.Empty(first.All.Intersect(second.All));
        Assert.EndsWith("@" + PiiCanaries.EmailDomain, first.Email, StringComparison.Ordinal);
        Assert.StartsWith(first.EmailLocalPart, first.Email, StringComparison.Ordinal);
    }

    private sealed class StubRecipients : INotificationRecipients
    {
        public static NotificationRecipient Known { get; } = new(UserId, Email: null, Mobile: null, AppLanguage.Arabic);

        public Task<NotificationRecipient?> FindAsync(long userId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == UserId ? Known : null);
    }
}
