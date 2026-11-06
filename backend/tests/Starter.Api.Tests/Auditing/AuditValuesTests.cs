using Codelab.Observability;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Auditing;
using Starter.Domain.Auditing;
using Starter.Infrastructure.Auditing;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Persistence;

namespace Starter.Api.Tests.Auditing;

/// <summary>Value formatting, masking per kind, the name conventions and the plans.</summary>
public sealed class AuditValuesTests
{
    [Theory]
    [InlineData(PersonalDataKind.NationalId, "NID-CANARY-4321", "***")]
    [InlineData(PersonalDataKind.Email, "person@example.test", "p***@example.test")]
    [InlineData(PersonalDataKind.Phone, "555-010-4567", "***67")]
    [InlineData(PersonalDataKind.Name, "A Person", "***")]
    [InlineData(PersonalDataKind.FreeText, "anything typed", "***")]
    [InlineData(PersonalDataKind.Email, null, null)]
    [InlineData(PersonalDataKind.Name, "  ", null)]
    public void Every_personal_data_kind_is_masked_like_the_logs(PersonalDataKind kind, string? value, string? expected) =>
        Assert.Equal(expected, AuditValues.Mask(kind, value));

    [Theory]
    [InlineData(PersonalDataKind.NationalId, PiiKind.Hidden)]
    [InlineData(PersonalDataKind.Email, PiiKind.Email)]
    [InlineData(PersonalDataKind.Phone, PiiKind.Phone)]
    [InlineData(PersonalDataKind.Name, PiiKind.Hidden)]
    [InlineData(PersonalDataKind.FreeText, PiiKind.Hidden)]
    public void Each_kind_maps_to_a_log_masking_kind(PersonalDataKind kind, PiiKind expected) =>
        Assert.Equal(expected, AuditValues.PiiKindOf(kind));

    [Fact]
    public void Values_are_formatted_invariantly_with_enums_as_integers_and_times_round_trip()
    {
        Assert.Null(AuditValues.Format(null));
        Assert.Equal("7", AuditValues.Format(ProbeState.Closed));
        Assert.Equal("true", AuditValues.Format(true));
        Assert.Equal("false", AuditValues.Format(false));
        Assert.Equal("1.5", AuditValues.Format(1.5m));
        Assert.Equal("2026-11-06T08:00:00.0000000+00:00", AuditValues.Format(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero)));
        Assert.Equal("2026-11-06", AuditValues.Format(new DateOnly(2026, 11, 6)));
        Assert.Equal("01:30:00", AuditValues.Format(TimeSpan.FromMinutes(90)));
        Assert.Equal("6f9619ff-8b86-d011-b42d-00c04fc964ff", AuditValues.Format(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff")));
    }

    [Theory]
    [InlineData("PasswordHash")]
    [InlineData("RefreshSecretHash")]
    [InlineData("IdTokenHint")]
    [InlineData("ActivationTicketId")]
    [InlineData("SecurityStamp")]
    [InlineData("passwordSalt")]
    public void Secret_names_are_recognized(string name) => Assert.True(AuditNamingConventions.IsSecret(name));

    [Theory]
    [InlineData("NationalId", PersonalDataKind.NationalId)]
    [InlineData("IqamaNumber", PersonalDataKind.NationalId)]
    [InlineData("NormalizedEmail", PersonalDataKind.Email)]
    [InlineData("MobileNumber", PersonalDataKind.Phone)]
    [InlineData("PhoneNumber", PersonalDataKind.Phone)]
    [InlineData("NormalizedUserName", PersonalDataKind.Name)]
    [InlineData("DisplayName", PersonalDataKind.Name)]
    [InlineData("NormalizedUpn", PersonalDataKind.Name)]
    [InlineData("RowsJson", PersonalDataKind.FreeText)]
    [InlineData("Message", PersonalDataKind.FreeText)]
    [InlineData("RejectionReason", PersonalDataKind.FreeText)]
    public void Personal_names_are_recognized_with_their_kind(string name, PersonalDataKind expected) =>
        Assert.Equal(expected, AuditNamingConventions.PersonalKindOf(name));

    [Theory]
    [InlineData("Name")]
    [InlineData("NameAr")]
    [InlineData("Code")]
    [InlineData("Description")]
    [InlineData("City")]
    public void Generic_labels_are_neither_secret_nor_personal(string name)
    {
        Assert.False(AuditNamingConventions.IsSecret(name));
        Assert.Null(AuditNamingConventions.PersonalKindOf(name));
    }

    [Fact]
    public void An_attribute_wins_over_the_name_conventions_and_only_text_follows_them()
    {
        using var context = AuditProbeContext.Offline();
        var plan = AuditPlans.For(context.Model.FindEntityType(typeof(ProbePerson))!);

        Assert.Equal(AuditPropertyTreatment.Masked, plan.For(nameof(ProbePerson.Contact)).Treatment);
        Assert.Equal(PersonalDataKind.Email, plan.For(nameof(ProbePerson.Contact)).Kind);
        Assert.Equal(AuditPropertyTreatment.Excluded, plan.For(nameof(ProbePerson.CacheTag)).Treatment);
        Assert.Equal(AuditPropertyTreatment.Recorded, plan.For(nameof(ProbePerson.State)).Treatment);
        Assert.Equal(AuditPropertyTreatment.Excluded, plan.For("NoSuchProperty").Treatment);
        Assert.Same(plan, AuditPlans.For(context.Model.FindEntityType(typeof(ProbePerson))!));
    }

    [Fact]
    public void The_changes_json_round_trips_and_keeps_arabic_readable()
    {
        IReadOnlyList<AuditPropertyChange> changes = [new("NameAr", OldValue: null, NewValue: "الإدارة", IsIdentifier: false), new("TeamId", "1", "2", IsIdentifier: true)];

        var json = AuditChangesJson.Serialize(changes);

        Assert.Contains("الإدارة", json, StringComparison.Ordinal);
        Assert.Contains("\"property\":\"NameAr\"", json, StringComparison.Ordinal);
        Assert.Equal(changes, AuditChangesJson.Deserialize(json));
        Assert.Empty(AuditChangesJson.Deserialize(" "));
    }

    [Fact]
    public void Over_long_texts_are_cut_to_their_columns_instead_of_failing_the_save()
    {
        var stamp = new AuditStamp(new string('a', AuditLimits.ActorMaxLength + 5), new string('c', AuditLimits.CorrelationIdMaxLength + 5), DateTimeOffset.UnixEpoch);

        var row = AuditEntry.From(new AuditEntryFacts(new string('t', AuditLimits.EntityTypeMaxLength + 1), new string('k', AuditLimits.EntityKeyMaxLength + 1), AuditAction.Added, "[]", stamp));

        Assert.Equal(AuditLimits.EntityTypeMaxLength, row.EntityType.Length);
        Assert.Equal(AuditLimits.EntityKeyMaxLength, row.EntityKey!.Length);
        Assert.Equal(AuditLimits.ActorMaxLength, row.Actor.Length);
        Assert.Equal(AuditLimits.CorrelationIdMaxLength, row.CorrelationId!.Length);
    }
}

/// <summary>The kernel registration: every module context carries the interceptor.</summary>
public sealed class AuditingRegistrationTests
{
    [Fact]
    public void The_kernel_registers_the_audit_trail_and_attaches_the_interceptor_to_every_module_context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStarterKernel(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Starter"] = "Server=(localdb)\\unused;Database=never-opened" })
            .Build());
        services.AddStarterAuditing();
        services.AddModuleDbContext<AuditProbeContext>(AuditProbeContext.SchemaName);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AuditProbeContext>();
        var interceptors = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!.Interceptors!;

        Assert.Contains(interceptors, interceptor => ReferenceEquals(interceptor, scope.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));
        Assert.IsType<EfAuditTrailQueries>(scope.ServiceProvider.GetRequiredService<IAuditTrailQueries>());
        Assert.IsType<SqlAuditTrailWriter>(scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IModuleDatabase) && descriptor.ImplementationType == typeof(ModuleDatabase<AuditDbContext>));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(AuditSaveChangesInterceptor));
    }
}
