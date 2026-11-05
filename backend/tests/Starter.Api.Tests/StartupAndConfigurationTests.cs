using Codelab.Errors;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Starter.Application.Results;
using Starter.Infrastructure.Configuration;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Startup;

namespace Starter.Api.Tests;

public sealed class StartupTaskRunnerTests
{
    [Fact]
    public async Task Migrations_run_first_then_tasks_in_phase_order()
    {
        var journal = new List<string>();
        var runner = Runner(journal, applyMigrations: true);

        await runner.StartAsync(CancellationToken.None);

        Assert.Equal(["migrate", "ReferenceData", "Accounts"], journal);
    }

    [Fact]
    public async Task Migrations_are_skipped_when_disabled()
    {
        var journal = new List<string>();
        var runner = Runner(journal, applyMigrations: false);

        await runner.StartAsync(CancellationToken.None);

        Assert.Equal(["ReferenceData", "Accounts"], journal);
    }

    [Fact]
    public async Task A_failing_task_stops_the_start()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStartupTask>(new FailingTask());
        var runner = new StartupTaskRunner(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DatabaseOptions()),
            NullLogger<StartupTaskRunner>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(CancellationToken.None));
    }

    [Fact]
    public void Phase_values_are_explicit_and_ordered()
    {
        Assert.Equal(1, (int)StartupPhase.ReferenceData);
        Assert.Equal(2, (int)StartupPhase.Accounts);
    }

    private static StartupTaskRunner Runner(List<string> journal, bool applyMigrations)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStartupTask>(new JournalTask(StartupPhase.Accounts, journal));
        services.AddSingleton<IStartupTask>(new JournalTask(StartupPhase.ReferenceData, journal));
        services.AddSingleton<IModuleDatabase>(new JournalDatabase(journal));
        return new StartupTaskRunner(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = applyMigrations }),
            NullLogger<StartupTaskRunner>.Instance);
    }

    private sealed class JournalTask(StartupPhase phase, List<string> journal) : IStartupTask
    {
        public StartupPhase Phase => phase;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            journal.Add(phase.ToString());
            return Task.CompletedTask;
        }
    }

    private sealed class JournalDatabase(List<string> journal) : IModuleDatabase
    {
        public string Name => "journal";

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            journal.Add("migrate");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingTask : IStartupTask
    {
        public StartupPhase Phase => StartupPhase.ReferenceData;

        public Task RunAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Startup failed.");
    }
}

public sealed class LocalSecretsFileTests
{
    [Fact]
    public void The_file_name_follows_the_environment()
    {
        Assert.Equal("appsettings.Development.local.json", LocalSecretsFile.FileNameFor(Environments.Development));
    }

    [Fact]
    public void In_Development_the_file_loads_right_after_the_environment_file()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });

        builder.AddLocalSecretsFile();

        var paths = builder.Configuration.Sources.OfType<JsonConfigurationSource>().Select(source => source.Path).ToList();
        var environmentFile = paths.IndexOf("appsettings.Development.json");
        Assert.Equal("appsettings.Development.local.json", paths[environmentFile + 1]);
    }

    [Fact]
    public void Outside_Development_the_file_is_never_loaded()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });

        builder.AddLocalSecretsFile();

        Assert.DoesNotContain(
            builder.Configuration.Sources.OfType<JsonConfigurationSource>(),
            source => source.Path == "appsettings.Production.local.json");
    }

    [Fact]
    public void The_platform_validation_code_matches_the_errors_package()
    {
        Assert.Equal(ErrorCodes.Validation, PlatformErrorCodes.Validation);
    }
}
