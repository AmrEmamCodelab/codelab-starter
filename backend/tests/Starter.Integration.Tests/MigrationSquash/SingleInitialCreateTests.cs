using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Identity.Infrastructure.Persistence;

namespace Starter.Integration.Tests.MigrationSquash;

/// <summary>
/// ADR-010 by reflection: every DbContext the host ships has exactly one migration, a class named InitialCreate (after
/// v1.0: InitialCreate first, then only migrations newer than <see cref="SquashedMigrations.ReleasedThroughMigrationId"/>).
/// One assertion lists every offending context, so a failure names all of them at once.
/// </summary>
public sealed class SingleInitialCreateTests
{
    private const string FirstId = "20260101000000_InitialCreate";
    private const string SecondId = "20260102000000_AddSomething";
    private const string ReleaseCutoff = "20260101500000_LastShipped";

    [Fact]
    public void Every_DbContext_has_exactly_one_migration_named_InitialCreate()
    {
        var contexts = SquashedMigrations.DbContexts();

        Assert.Contains(typeof(IdentityDbContext), contexts); // the discovery reaches the module assemblies
        var offenders = contexts
            .Select(context => (context.Name, Problem: SquashedMigrations.Violation(SquashedMigrations.MigrationsOf(context), SquashedMigrations.ReleasedThroughMigrationId)))
            .Where(result => result.Problem is not null)
            .Select(result => $"{result.Name}: {result.Problem}")
            .ToList();
        Assert.True(offenders.Count == 0, $"{offenders.Count} of {contexts.Count} DbContexts break ADR-010:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void A_single_InitialCreate_passes() =>
        Assert.Null(SquashedMigrations.Violation([Info(FirstId, typeof(InitialCreate))], releasedThrough: null));

    [Fact]
    public void No_migration_at_all_fails() =>
        Assert.NotNull(SquashedMigrations.Violation([], releasedThrough: null));

    [Fact]
    public void A_second_migration_before_v1_is_not_folded() =>
        Assert.Contains(SecondId, SquashedMigrations.Violation([Info(FirstId, typeof(InitialCreate)), Info(SecondId, typeof(AddSomething))], releasedThrough: null));

    [Fact]
    public void After_v1_a_newer_incremental_migration_passes() =>
        Assert.Null(SquashedMigrations.Violation([Info(FirstId, typeof(InitialCreate)), Info(SecondId, typeof(AddSomething))], releasedThrough: ReleaseCutoff));

    [Fact]
    public void After_v1_a_migration_inside_the_release_is_not_folded() =>
        Assert.NotNull(SquashedMigrations.Violation([Info(FirstId, typeof(InitialCreate)), Info(SecondId, typeof(AddSomething))], releasedThrough: SecondId));

    [Fact]
    public void A_chain_that_does_not_start_with_InitialCreate_fails() =>
        Assert.NotNull(SquashedMigrations.Violation([Info(FirstId, typeof(AddSomething)), Info(SecondId, typeof(InitialCreate))], releasedThrough: null));

    [Fact]
    public void A_migration_without_an_id_fails() =>
        Assert.NotNull(SquashedMigrations.Violation([Info(string.Empty, typeof(InitialCreate))], releasedThrough: null));

    private static MigrationInfo Info(string id, Type type) => new(Id: id, Type: type);

    /// <summary>A stand-in named like the squashed migration (never applied; no attributes).</summary>
    private sealed class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }
    }

    /// <summary>A stand-in for a later migration (never applied; no attributes).</summary>
    private sealed class AddSomething : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }
    }
}
