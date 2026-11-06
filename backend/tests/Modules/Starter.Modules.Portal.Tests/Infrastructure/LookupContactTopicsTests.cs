using Starter.Application.Lookups;
using Starter.Domain.Lookups;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Infrastructure.Lookups;
using Starter.Modules.Portal.Tests.Fakes;

namespace Starter.Modules.Portal.Tests.Infrastructure;

/// <summary>
/// The Portal reads contact topics only through the kernel <see cref="ILookupValues"/> port (the Lookups module's public
/// contract), never the Lookups tables.
/// </summary>
public sealed class LookupContactTopicsTests
{
    private const string GeneralCode = "general";
    private const string GeneralNameAr = "عام";
    private const string GeneralNameEn = "General";

    private readonly FakeLookupValues lookups = new();

    [Fact]
    public async Task An_active_value_is_mapped_to_a_contact_topic()
    {
        lookups.Values[(LookupTypes.ContactTopics, ContactTopicIds.General)] =
            new ActiveLookupValue(ContactTopicIds.General, GeneralCode, GeneralNameAr, GeneralNameEn);

        var found = await NewTopics().FindActiveAsync(ContactTopicIds.General, CancellationToken.None);

        Assert.Equal(new ContactTopic(ContactTopicIds.General, GeneralNameAr, GeneralNameEn), found);
    }

    [Fact]
    public async Task An_unknown_or_retired_value_is_null()
    {
        // The port answers null for an unknown id and for a retired value alike (it serves only active values).
        var found = await NewTopics().FindActiveAsync(ContactTopicIds.Account, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task A_value_of_another_lookup_type_with_the_same_id_is_not_a_topic()
    {
        const string otherType = "other-type";
        lookups.Values[(otherType, ContactTopicIds.Request)] =
            new ActiveLookupValue(ContactTopicIds.Request, GeneralCode, GeneralNameAr, GeneralNameEn);

        var found = await NewTopics().FindActiveAsync(ContactTopicIds.Request, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task The_port_is_asked_for_the_contact_topics_type_and_the_given_id()
    {
        await NewTopics().FindActiveAsync(ContactTopicIds.Suggestion, CancellationToken.None);

        var asked = Assert.Single(lookups.Asked);
        Assert.Equal(LookupTypes.ContactTopics, asked.TypeKey);
        Assert.Equal(ContactTopicIds.Suggestion, asked.Id);
    }

    private LookupContactTopics NewTopics() => new(lookups);
}
