using Microsoft.Extensions.Options;
using Package.Sample.Api.Authentication;
using Package.Sample.Api.Hosting;
using Package.Sample.Api.Records;
using Package.Sample.Tests.Support;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>Unit tests of the sample's own logic: validation, the store, options and routes.</summary>
public sealed class SampleLogicTests
{
    [Fact]
    public void A_valid_request_has_no_errors()
    {
        Assert.Empty(RecordRules.Validate(new CreateRecordRequest("Title", "someone@example.test")));
    }

    [Theory]
    [InlineData(null, "someone@example.test", RecordRules.TitleField, RecordRules.Required)]
    [InlineData("  ", "someone@example.test", RecordRules.TitleField, RecordRules.Required)]
    [InlineData("Title", null, RecordRules.ContactEmailField, RecordRules.Required)]
    [InlineData("Title", "no-at-sign", RecordRules.ContactEmailField, RecordRules.NotAnEmail)]
    [InlineData("Title", "@example.test", RecordRules.ContactEmailField, RecordRules.NotAnEmail)]
    [InlineData("Title", "a@b@example.test", RecordRules.ContactEmailField, RecordRules.NotAnEmail)]
    [InlineData("Title", "someone@", RecordRules.ContactEmailField, RecordRules.NotAnEmail)]
    public void An_invalid_field_is_reported_under_its_key(string? title, string? email, string field, string message)
    {
        var errors = RecordRules.Validate(new CreateRecordRequest(title, email));

        Assert.Equal([message], errors[field]);
    }

    [Fact]
    public void Every_field_error_is_reported_at_once_and_titles_have_a_length_limit()
    {
        var errors = RecordRules.Validate(new CreateRecordRequest(new string('x', RecordRules.MaxTitleLength + 1), ContactEmail: null));

        Assert.Equal([RecordRules.TooLong], errors[RecordRules.TitleField]);
        Assert.Equal([RecordRules.Required], errors[RecordRules.ContactEmailField]);
        Assert.Empty(RecordRules.Validate(new CreateRecordRequest(new string('x', RecordRules.MaxTitleLength), "a@b")));
    }

    [Fact]
    public void The_store_holds_the_seed_assigns_increasing_ids_and_archives()
    {
        var store = new RecordStore();

        var first = store.Add("  First  ");
        var second = store.Add("Second");

        Assert.Equal(RecordStore.SeedTitle, store.Find(RecordStore.SeedRecordId)!.Title);
        Assert.Equal("First", first.Title);
        Assert.True(second.Id > first.Id && first.Id > RecordStore.SeedRecordId);
        Assert.True(store.MarkArchived(first.Id));
        Assert.True(store.Find(first.Id)!.Archived);
        Assert.False(store.MarkArchived(second.Id + 1));
        Assert.Null(store.Find(second.Id + 1));
        Assert.Throws<ArgumentException>(() => store.Add(" "));
    }

    [Fact]
    public void Authentication_options_need_issuer_audience_a_long_key_and_a_positive_lifetime()
    {
        var validator = new SampleAuthenticationOptionsValidator();
        var valid = new SampleAuthenticationOptions { Issuer = "i", Audience = "a", SigningKeyBase64 = SampleKeys.NewKey() };
        var invalid = new SampleAuthenticationOptions { SigningKeyBase64 = Convert.ToBase64String(new byte[16]), SessionLifetimeMinutes = 0 };

        Assert.True(validator.Validate(Options.DefaultName, valid).Succeeded);
        var failure = validator.Validate(Options.DefaultName, invalid);

        Assert.True(failure.Failed);
        Assert.Equal(4, failure.Failures!.Count());
        Assert.True(validator.Validate(Options.DefaultName, valid.WithKey("not base64!")).Failed);
        Assert.Equal(TimeSpan.FromMinutes(SampleAuthenticationOptions.DefaultSessionLifetimeMinutes), valid.SessionLifetime);
    }

    [Fact]
    public void Routes_are_built_from_the_route_constants()
    {
        Assert.Equal("/api/records/t1", SampleRoutes.RecordFor("t1"));
        Assert.Equal("/api/records/t1/archive", SampleRoutes.ArchiveFor("t1"));
        Assert.Equal("/api/internal/records/t1/touch", SampleRoutes.InternalTouchFor("t1"));
        Assert.Equal("/api/records/{id}/archive", SampleRoutes.ArchiveRecord);
        Assert.Equal("/api/internal/records/{id}/touch", SampleRoutes.InternalTouch);
    }
}

internal static class SampleAuthenticationOptionsExtensions
{
    public static SampleAuthenticationOptions WithKey(this SampleAuthenticationOptions options, string key) => new()
    {
        Issuer = options.Issuer,
        Audience = options.Audience,
        SigningKeyBase64 = key,
        SessionLifetimeMinutes = options.SessionLifetimeMinutes,
    };
}
