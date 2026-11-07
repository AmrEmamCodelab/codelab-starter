using Codelab.Security.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure;
using Starter.Modules.Identity.Infrastructure.Persistence;
using Starter.Modules.Identity.Infrastructure.Security;

namespace Starter.Modules.Identity.Tests.Infrastructure;

public sealed class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void A_hash_verifies_its_password_only()
    {
        var hash = _hasher.Hash("pa55word");

        Assert.NotEqual("pa55word", hash);
        Assert.Equal(PasswordVerification.Succeeded, _hasher.Verify(hash, "pa55word"));
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify(hash, "Pa55word"));
    }

    [Fact]
    public void No_hash_fails_after_checking_the_decoy()
    {
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify(passwordHash: null, "anything"));
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("not a hash", "anything"));
    }
}

public sealed class SecureTokenGeneratorTests
{
    [Fact]
    public void Ids_and_secrets_have_the_refresh_token_shape_and_differ()
    {
        var generator = new SecureTokenGenerator();
        var first = new RefreshToken(generator.NewSessionId(), generator.NewRefreshSecret());
        var second = new RefreshToken(generator.NewSessionId(), generator.NewRefreshSecret());

        Assert.True(RefreshToken.TryParse(first.Format(), out _));
        Assert.Equal(IdentityLimits.SessionIdLength, first.SessionId.Length);
        Assert.NotEqual(first, second);
    }
}

public sealed class SessionStoreAdapterTests
{
    [Fact]
    public async Task Revocation_and_versions_use_the_security_stores_keyed_by_subject()
    {
        var revocations = new MemorySessionRevocationStore(TimeProvider.System);
        var versions = new MemorySessionVersionStore();
        var subject = Guid.NewGuid();
        await versions.BumpAsync(subject.ToString(), CancellationToken.None);

        await new SessionRevocationAdapter(revocations).RevokeAsync("abc", TimeProvider.System.GetUtcNow().AddMinutes(5), CancellationToken.None);

        Assert.True(await revocations.IsRevokedAsync("abc", CancellationToken.None));
        Assert.Equal(1, await new SessionVersionsAdapter(versions).CurrentAsync(subject, CancellationToken.None));
    }
}

public sealed class IdentityModelTests
{
    [Fact]
    public void The_model_lives_in_the_identity_schema_and_matches_its_migrations()
    {
        using var context = NewContext();

        Assert.Equal(IdentityDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(TableNames.UserAccounts, context.Model.FindEntityType(typeof(UserAccount))!.GetTableName());
        Assert.Equal(TableNames.UserSessions, context.Model.FindEntityType(typeof(UserSession))!.GetTableName());
        Assert.Equal(TableNames.AuthEvents, context.Model.FindEntityType(typeof(AuthEvent))!.GetTableName());
        Assert.Equal(
            [Starter.Modules.Identity.Infrastructure.Persistence.Migrations.IdentityMigrationIds.InitialCreate],
            context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_module_is_named_after_its_assembly() =>
        Assert.Equal("Identity", new IdentityModule().Name);

    private static IdentityDbContext NewContext() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, IdentityDbContext.SchemaName))
        .Options);
}
