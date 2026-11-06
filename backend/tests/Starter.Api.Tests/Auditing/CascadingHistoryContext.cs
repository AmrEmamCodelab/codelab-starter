using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;

namespace Starter.Api.Tests.Auditing.Cascading;

/// <summary>
/// A module context that maps a record history by hand with a delete behavior that would take the history with its
/// record (the defect the guard refuses). Its own namespace keeps the probe configurations (HasRecordHistory) away.
/// </summary>
public sealed class CascadingHistoryContext() : ModuleDbContext(new DbContextOptionsBuilder<CascadingHistoryContext>()
    .UseSqlServer("Server=.;Database=NeverOpened")
    .ReplaceService<IModelCacheKeyFactory, PerBehaviorModelCacheKey>()
    .Options)
{
    public static DeleteBehavior Behavior { get; set; } = DeleteBehavior.Cascade;

    protected override string Schema => "cascading";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbePerson>().Ignore(person => person.TeamId);
        modelBuilder.Entity<ProbeHistory>(MapCascading);
        base.OnModelCreating(modelBuilder);
    }

    private static void MapCascading(EntityTypeBuilder<ProbeHistory> history) =>
        history.HasOne<ProbePerson>().WithMany().HasForeignKey(entry => entry.RecordId).OnDelete(Behavior);
}

/// <summary>One cached model per delete behavior, so each theory row builds its own.</summary>
public sealed class PerBehaviorModelCacheKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), CascadingHistoryContext.Behavior, designTime);
}
