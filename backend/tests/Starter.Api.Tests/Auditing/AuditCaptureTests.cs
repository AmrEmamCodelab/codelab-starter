using Starter.Application.Auditing;
using Starter.Domain.Auditing;
using Starter.Infrastructure.Auditing;

namespace Starter.Api.Tests.Auditing;

/// <summary>What the change tracker becomes in the trail: masking of every personal-data kind, exclusions, identifiers.</summary>
public sealed class AuditCaptureTests
{
    private const string NationalNumber = "NID-CANARY-4321";
    private const string Email = "canary.person@example.test";
    private const string Phone = "555-010-4567";
    private const string PersonName = "Canary Person Name";
    private const string FreeText = "free text that names Canary Person";
    private static readonly AuditStamp Stamp = new("SYSTEM", "corr-1", new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void An_insert_records_every_audited_property_with_personal_data_masked_by_kind()
    {
        using var context = AuditProbeContext.Offline();
        context.People.Add(NewPerson());

        var row = Assert.Single(AuditCapture.Take(context, Stamp).Complete());
        var changes = Changes(row);

        Assert.Equal(AuditAction.Added, row.Action);
        Assert.Equal(nameof(ProbePerson), row.EntityType);
        Assert.Equal("5", row.EntityKey);
        Assert.Equal("***", changes[nameof(ProbePerson.NationalNumber)].NewValue);
        Assert.Equal("c***@example.test", changes[nameof(ProbePerson.Contact)].NewValue);
        Assert.Equal("***67", changes[nameof(ProbePerson.Cell)].NewValue);
        Assert.Equal("***", changes[nameof(ProbePerson.Label)].NewValue);
        Assert.Equal("***", changes[nameof(ProbePerson.Remarks)].NewValue);
        Assert.Equal("Riyadh", changes[nameof(ProbePerson.City)].NewValue);
        Assert.All(changes.Values, change => Assert.Null(change.OldValue));
    }

    [Fact]
    public void The_trail_never_holds_a_planted_national_id_email_phone_name_or_free_text()
    {
        using var context = AuditProbeContext.Offline();
        context.People.Add(NewPerson());

        var json = Assert.Single(AuditCapture.Take(context, Stamp).Complete()).ChangesJson;

        foreach (var canary in new[] { NationalNumber, Email, "010-4567", PersonName, FreeText, "canary.person" })
        {
            Assert.DoesNotContain(canary, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NotAudited_properties_secrets_by_name_and_binary_values_are_left_out()
    {
        using var context = AuditProbeContext.Offline();
        context.People.Add(NewPerson());

        var changes = Changes(Assert.Single(AuditCapture.Take(context, Stamp).Complete()));

        Assert.DoesNotContain(nameof(ProbePerson.CacheTag), changes.Keys);
        Assert.DoesNotContain(nameof(ProbePerson.PasswordHash), changes.Keys);
        Assert.DoesNotContain(nameof(ProbePerson.Photo), changes.Keys);
        Assert.Equal("C***@EXAMPLE.TEST", changes[nameof(ProbePerson.NormalizedEmail)].NewValue);
    }

    [Fact]
    public void Keys_and_foreign_keys_are_flagged_as_identifiers_and_enums_are_recorded_as_their_integer()
    {
        using var context = AuditProbeContext.Offline();
        context.People.Add(NewPerson());

        var changes = Changes(Assert.Single(AuditCapture.Take(context, Stamp).Complete()));

        Assert.True(changes[nameof(ProbePerson.Id)].IsIdentifier);
        Assert.True(changes[nameof(ProbePerson.TeamId)].IsIdentifier);
        Assert.Equal("9", changes[nameof(ProbePerson.TeamId)].NewValue);
        Assert.False(changes[nameof(ProbePerson.City)].IsIdentifier);
        Assert.Equal("7", changes[nameof(ProbePerson.State)].NewValue);
    }

    [Fact]
    public void An_update_records_only_the_properties_whose_value_changed_old_and_new_masked()
    {
        using var context = AuditProbeContext.Offline();
        var person = NewPerson();
        context.People.Attach(person);
        person.Contact = "other.address@example.test";
        person.City = "Jeddah";
        context.Entry(person).Property(entry => entry.Label).IsModified = true;

        var row = Assert.Single(AuditCapture.Take(context, Stamp).Complete());
        var changes = Changes(row);

        Assert.Equal(AuditAction.Modified, row.Action);
        Assert.Equal([nameof(ProbePerson.City), nameof(ProbePerson.Contact)], changes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("c***@example.test", changes[nameof(ProbePerson.Contact)].OldValue);
        Assert.Equal("o***@example.test", changes[nameof(ProbePerson.Contact)].NewValue);
        Assert.Equal("Riyadh", changes[nameof(ProbePerson.City)].OldValue);
        Assert.Equal("Jeddah", changes[nameof(ProbePerson.City)].NewValue);
    }

    [Fact]
    public void An_update_of_only_excluded_properties_writes_no_row()
    {
        using var context = AuditProbeContext.Offline();
        var person = NewPerson();
        context.People.Attach(person);
        person.CacheTag = "changed";
        person.PasswordHash = "AQAAAAEAACcQ-new";

        Assert.True(AuditCapture.Take(context, Stamp).IsEmpty);
    }

    [Fact]
    public void A_delete_records_the_original_values_under_the_original_key()
    {
        using var context = AuditProbeContext.Offline();
        var person = NewPerson();
        context.People.Attach(person);
        context.People.Remove(person);

        var row = Assert.Single(AuditCapture.Take(context, Stamp).Complete());
        var changes = Changes(row);

        Assert.Equal(AuditAction.Deleted, row.Action);
        Assert.Equal("5", row.EntityKey);
        Assert.Equal("Riyadh", changes[nameof(ProbePerson.City)].OldValue);
        Assert.Equal("***", changes[nameof(ProbePerson.NationalNumber)].OldValue);
        Assert.All(changes.Values, change => Assert.Null(change.NewValue));
    }

    [Fact]
    public void A_composite_key_is_recorded_as_a_json_object_of_its_parts()
    {
        using var context = AuditProbeContext.Offline();
        context.Memberships.Add(new ProbeMembership { PersonId = 5, TeamId = 9, IsLead = true });

        var row = Assert.Single(AuditCapture.Take(context, Stamp).Complete());

        Assert.Equal("""{"PersonId":"5","TeamId":"9"}""", row.EntityKey);
        Assert.Equal("true", Changes(row)[nameof(ProbeMembership.IsLead)].NewValue);
    }

    [Fact]
    public void NotAudited_entities_and_record_history_rows_write_no_row()
    {
        using var context = AuditProbeContext.Offline();
        context.Heartbeats.Add(new ProbeHeartbeat { Id = 1, At = Stamp.At });
        context.History.Add(new ProbeHistory(new RecordHistoryFacts(5, "submit", FromStatus: null, ToStatus: 2, Reason: null, ActorUserId: null, ActorName: "SYSTEM", Stamp.At)));

        Assert.True(AuditCapture.Take(context, Stamp).IsEmpty);
    }

    [Fact]
    public void Every_row_carries_the_stamp_of_the_save()
    {
        using var context = AuditProbeContext.Offline();
        context.Teams.Add(new ProbeTeam { Name = "Team" });

        var row = Assert.Single(AuditCapture.Take(context, Stamp).Complete());

        Assert.Equal(Stamp.Actor, row.Actor);
        Assert.Equal(Stamp.CorrelationId, row.CorrelationId);
        Assert.Equal(Stamp.At, row.At);
    }

    internal static ProbePerson NewPerson() => new()
    {
        Id = 5,
        NationalNumber = NationalNumber,
        Contact = Email,
        Cell = Phone,
        Label = PersonName,
        Remarks = FreeText,
        City = "Riyadh",
        CacheTag = "tag",
        PasswordHash = "AQAAAAEAACcQ-secret",
        NormalizedEmail = Email.ToUpperInvariant(),
        Photo = [1, 2, 3],
        State = ProbeState.Closed,
        TeamId = 9,
    };

    internal static Dictionary<string, AuditPropertyChange> Changes(AuditEntry row) =>
        AuditChangesJson.Deserialize(row.ChangesJson).ToDictionary(change => change.Property, StringComparer.Ordinal);
}
