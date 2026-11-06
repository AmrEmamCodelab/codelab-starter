using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Domain;
using Starter.Modules.Lookups.Tests.Fakes;

namespace Starter.Modules.Lookups.Tests.Domain;

public sealed class LookupEntityTests
{
    [Fact]
    public void A_seeded_value_carries_its_seed_and_is_active()
    {
        var item = LookupItem.Seeded(LookupTypes.ContactTopics, new LookupItemSeed(
            Id: ContactTopicIds.Request, Code: "request", Names: new LookupNames(Ar: "طلب", En: "Request"), SortOrder: 20));

        Assert.Equal(LookupTypes.ContactTopics, item.TypeKey);
        Assert.Equal(ContactTopicIds.Request, item.Id);
        Assert.Equal("request", item.Code);
        Assert.Equal("طلب", item.NameAr);
        Assert.Equal("Request", item.NameEn);
        Assert.Equal(20, item.SortOrder);
        Assert.True(item.IsActive);
    }

    [Fact]
    public void SetActive_reports_a_change_only_when_the_state_changes()
    {
        var item = FakeLookupRepository.Item(LookupTypes.ContactTopics, ContactTopicIds.General, "general", sortOrder: 10);

        Assert.False(item.SetActive(true));
        Assert.True(item.SetActive(false));
        Assert.False(item.IsActive);
        Assert.False(item.SetActive(false));
        Assert.True(item.SetActive(true));
        Assert.True(item.IsActive);
    }

    [Fact]
    public void A_seeded_type_carries_its_key_and_names()
    {
        var type = LookupType.Seeded(LookupTypes.ContactTopics, new LookupNames(Ar: "مواضيع", En: "Topics"));

        Assert.Equal((LookupTypes.ContactTopics, "مواضيع", "Topics"), (type.Key, type.NameAr, type.NameEn));
    }

    [Fact]
    public void Seeding_refuses_a_blank_key() =>
        Assert.Throws<ArgumentException>(() => LookupType.Seeded(" ", new LookupNames(Ar: "a", En: "b")));
}

public sealed class LookupLimitsTests
{
    [Theory]
    [InlineData("contact-topics", true)]
    [InlineData("a", true)]
    [InlineData("type2", true)]
    [InlineData("a1-b2-c3", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Contact-topics", false)]
    [InlineData("1type", false)]
    [InlineData("-type", false)]
    [InlineData("type-", false)]
    [InlineData("contact--topics", false)]
    [InlineData("contact_topics", false)]
    [InlineData("contact topics", false)]
    [InlineData("مواضيع", false)]
    [InlineData("../x", false)]
    public void IsKebab_accepts_lower_case_words_joined_by_single_hyphens(string? key, bool expected) =>
        Assert.Equal(expected, LookupLimits.IsKebab(key, LookupLimits.TypeKeyMaxLength));

    [Fact]
    public void IsKebab_refuses_a_key_longer_than_the_limit()
    {
        Assert.True(LookupLimits.IsKebab(new string('a', LookupLimits.TypeKeyMaxLength), LookupLimits.TypeKeyMaxLength));
        Assert.False(LookupLimits.IsKebab(new string('a', LookupLimits.TypeKeyMaxLength + 1), LookupLimits.TypeKeyMaxLength));
    }

    [Fact]
    public void Every_seeded_type_key_is_well_formed()
    {
        var keys = typeof(LookupTypes).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(LookupLimits.IsKebab(key, LookupLimits.TypeKeyMaxLength), key));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
