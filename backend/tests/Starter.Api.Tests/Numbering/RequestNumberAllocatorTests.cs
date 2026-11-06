using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Starter.Api.Tests.Messaging;
using Starter.Application.Numbering;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Numbering;
using Starter.Infrastructure.Numbering.Migrations;
using Starter.Infrastructure.Persistence;

namespace Starter.Api.Tests.Numbering;

/// <summary>The allocator without a database: the year in the configured time zone, one sequence per prefix and year.</summary>
public sealed class RequestNumberAllocatorTests
{
    private readonly CountingSequences _sequences = new();

    [Fact]
    public async Task The_first_number_of_a_prefix_in_a_year_is_001_and_the_next_002()
    {
        var allocator = Allocator(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal("CU-2026-001", await allocator.NextAsync("CU", CancellationToken.None));
        Assert.Equal("CU-2026-002", await allocator.NextAsync("CU", CancellationToken.None));
    }

    [Fact]
    public async Task Each_prefix_counts_on_its_own_sequence()
    {
        var allocator = Allocator(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        await allocator.NextAsync("CU", CancellationToken.None);
        var record = await allocator.NextAsync("SR", CancellationToken.None);

        Assert.Equal("SR-2026-001", record);
        Assert.Equal(["CU_2026", "SR_2026"], _sequences.Names);
    }

    [Fact]
    public async Task The_counter_grows_past_999_without_truncation()
    {
        _sequences.Seed("CU_2026", lastValue: 999);
        var allocator = Allocator(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal("CU-2026-1000", await allocator.NextAsync("CU", CancellationToken.None));
    }

    [Fact]
    public async Task The_year_rolls_over_at_midnight_in_the_configured_time_zone_not_in_utc()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 12, 31, 20, 59, 0, TimeSpan.Zero));
        var allocator = Allocator(clock);

        var lastOfYear = await allocator.NextAsync("CU", CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        var firstOfNextYear = await allocator.NextAsync("CU", CancellationToken.None);

        Assert.Equal("CU-2026-001", lastOfYear);
        Assert.Equal("CU-2027-001", firstOfNextYear);
        Assert.Equal(["CU_2026", "CU_2027"], _sequences.Names);
    }

    [Fact]
    public async Task A_utc_time_zone_keeps_the_utc_year()
    {
        var allocator = Allocator(new ManualClock(new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.Zero)), TimeZoneInfo.Utc.Id);

        Assert.Equal("CU-2026-001", await allocator.NextAsync("CU", CancellationToken.None));
    }

    [Theory]
    [InlineData("cu")]
    [InlineData("C")]
    [InlineData("CU1")]
    [InlineData("CU'; DROP")]
    [InlineData("")]
    public async Task An_invalid_prefix_is_refused_before_any_sequence_is_touched(string prefix)
    {
        var allocator = Allocator(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<ArgumentException>(() => allocator.NextAsync(prefix, CancellationToken.None));

        Assert.Empty(_sequences.Names);
    }

    [Fact]
    public void A_sequence_name_is_prefix_underscore_year()
    {
        Assert.Equal("CU_2026", RequestNumberAllocator.SequenceName("CU", 2026));
        Assert.Throws<ArgumentException>(() => RequestNumberAllocator.SequenceName("cu", 2026));
        Assert.Throws<ArgumentOutOfRangeException>(() => RequestNumberAllocator.SequenceName("CU", 99999));
    }

    [Theory]
    [InlineData("CU_2026", true)]
    [InlineData("A1_b", true)]
    [InlineData("CU-2026", false)]
    [InlineData("CU]; DROP", false)]
    [InlineData(" ", false)]
    public void Only_letters_digits_and_underscores_reach_the_sql(string name, bool safe)
    {
        var refusal = Record.Exception(() => SqlNumberSequences.EnsureSafeName(name));

        Assert.Equal(safe, refusal is null);
    }

    [Fact]
    public void A_sequence_name_longer_than_the_limit_is_refused() =>
        Assert.Throws<ArgumentException>(() => SqlNumberSequences.EnsureSafeName(new string('A', SqlNumberSequences.MaxNameLength + 1)));

    private RequestNumberAllocator Allocator(DateTimeOffset now) => Allocator(new ManualClock(now));

    private RequestNumberAllocator Allocator(TimeProvider clock, string timeZoneId = NumberingOptions.DefaultTimeZoneId) =>
        new(_sequences, clock, Options.Create(new NumberingOptions { TimeZoneId = timeZoneId }));

    private sealed class CountingSequences : INumberSequences
    {
        private readonly Dictionary<string, long> _values = new(StringComparer.Ordinal);

        public List<string> Names { get; } = [];

        public void Seed(string name, long lastValue) => _values[name] = lastValue;

        public Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken)
        {
            if (!Names.Contains(sequenceName))
            {
                Names.Add(sequenceName);
            }

            _values[sequenceName] = _values.GetValueOrDefault(sequenceName) + 1;
            return Task.FromResult(_values[sequenceName]);
        }
    }
}

/// <summary>The numbering options, schema, migration and registration.</summary>
public sealed class NumberingRegistrationTests
{
    [Fact]
    public void The_default_time_zone_is_riyadh_and_it_exists_on_this_machine()
    {
        var options = new NumberingOptions();

        Assert.Equal("Arab Standard Time", options.TimeZoneId);
        Assert.True(new NumberingOptionsValidator().Validate(name: null, options).Succeeded);
    }

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("")]
    public void An_unknown_time_zone_refuses_the_options(string timeZoneId) =>
        Assert.True(new NumberingOptionsValidator().Validate(name: null, new NumberingOptions { TimeZoneId = timeZoneId }).Failed);

    [Fact]
    public void The_registration_binds_numbering_and_resolves_the_allocator_without_a_database()
    {
        using var provider = Provider(new Dictionary<string, string?> { ["Numbering:TimeZoneId"] = "UTC" });
        using var scope = provider.CreateScope();

        Assert.Equal("UTC", provider.GetRequiredService<IOptions<NumberingOptions>>().Value.TimeZoneId);
        Assert.IsType<RequestNumberAllocator>(scope.ServiceProvider.GetRequiredService<IRequestNumberAllocator>());
        Assert.Single(scope.ServiceProvider.GetServices<IModuleDatabase>(), database => database.Name == nameof(NumberingDbContext));
    }

    [Fact]
    public void A_second_registration_adds_nothing_so_the_schema_is_migrated_once()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddStarterNumbering(configuration).AddStarterNumbering(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IModuleDatabase));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRequestNumberAllocator));
    }

    [Fact]
    public void A_bad_time_zone_in_configuration_refuses_the_options()
    {
        using var provider = Provider(new Dictionary<string, string?> { ["Numbering:TimeZoneId"] = "Nowhere/Never" });

        var refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NumberingOptions>>().Value);
        Assert.Contains("Numbering:TimeZoneId", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_schema_migration_creates_the_numbering_schema_with_its_own_history_and_no_table()
    {
        using var context = new NumberingDbContext(new DbContextOptionsBuilder<NumberingDbContext>()
            .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, NumberingDbContext.SchemaName))
            .Options);

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Equal([NumberingMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains("CREATE SCHEMA [numbering]", script, StringComparison.Ordinal);
        Assert.Contains($"[numbering].[{PersistenceRegistration.MigrationsHistoryTable}]", script, StringComparison.Ordinal);
        Assert.Single(script.Split("CREATE TABLE ", StringSplitOptions.None).Skip(1));
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Starter"] = "Server=(localdb)\\unused;Database=never-opened";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddStarterKernel(configuration);
        services.AddStarterNumbering(configuration);
        return services.BuildServiceProvider();
    }
}
