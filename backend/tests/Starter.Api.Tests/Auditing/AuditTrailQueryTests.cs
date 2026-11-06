using Microsoft.EntityFrameworkCore;
using Starter.Application.Auditing;
using Starter.Domain.Auditing;
using Starter.Infrastructure.Auditing;

namespace Starter.Api.Tests.Auditing;

/// <summary>The viewer query's filter and row mapping, over rows in memory (the SQL page is integration-tested).</summary>
public sealed class AuditTrailQueryTests
{
    private static readonly AuditStamp Stamp = new("SYSTEM", "corr-9", new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void No_filter_keeps_every_row()
    {
        var rows = Rows();

        Assert.Equal(4, EfAuditTrailQueries.Filter(rows.AsQueryable(), new AuditTrailQuery(EntityType: null, EntityKey: null, BeforeId: null, Take: 20)).Count());
    }

    [Fact]
    public void The_type_and_key_filters_select_one_entity_trail()
    {
        var rows = Rows();

        var trail = EfAuditTrailQueries.Filter(rows.AsQueryable(), new AuditTrailQuery("Organization", "5", BeforeId: null, Take: 20)).ToList();

        Assert.Equal([1L, 3L], trail.Select(row => row.Id));
    }

    [Fact]
    public void A_key_without_a_type_is_ignored()
    {
        var rows = Rows();

        Assert.Equal(4, EfAuditTrailQueries.Filter(rows.AsQueryable(), new AuditTrailQuery(EntityType: null, "5", BeforeId: null, Take: 20)).Count());
    }

    [Fact]
    public void The_cursor_keeps_only_older_rows()
    {
        var rows = Rows();

        var older = EfAuditTrailQueries.Filter(rows.AsQueryable(), new AuditTrailQuery(EntityType: null, EntityKey: null, BeforeId: 3, Take: 20)).ToList();

        Assert.Equal([1L, 2L], older.Select(row => row.Id));
    }

    [Fact]
    public void A_row_maps_to_the_port_view_with_its_changes()
    {
        var row = Rows()[0];

        var entry = EfAuditTrailQueries.ToEntry(row);

        Assert.Equal(1, entry.Id);
        Assert.Equal("Organization", entry.EntityType);
        Assert.Equal("5", entry.EntityKey);
        Assert.Equal(AuditAction.Modified, entry.Action);
        Assert.Equal(AuditActors.System, entry.Actor);
        Assert.Equal("corr-9", entry.CorrelationId);
        Assert.Equal(Stamp.At, entry.At);
        Assert.Equal(new AuditPropertyChange("City", "Riyadh", "Jeddah", IsIdentifier: false), Assert.Single(entry.Changes));
    }

    private static List<AuditEntry> Rows()
    {
        using var context = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer("Server=.;Database=NeverOpened").Options);
        List<AuditEntry> rows =
        [
            Row("Organization", "5", [new("City", "Riyadh", "Jeddah", IsIdentifier: false)]),
            Row("Organization", "6", []),
            Row("Organization", "5", []),
            Row("Role", "5", []),
        ];
        for (var index = 0; index < rows.Count; index++)
        {
            context.Entries.Attach(rows[index]).Property(entry => entry.Id).CurrentValue = index + 1;
        }

        return rows;
    }

    private static AuditEntry Row(string type, string key, IReadOnlyList<AuditPropertyChange> changes) =>
        AuditEntry.From(new AuditEntryFacts(type, key, AuditAction.Modified, AuditChangesJson.Serialize(changes), Stamp));
}
