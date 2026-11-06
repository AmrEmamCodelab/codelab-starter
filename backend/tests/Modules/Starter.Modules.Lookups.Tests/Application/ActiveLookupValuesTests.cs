using Starter.Application.Lookups;
using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Tests.Fakes;

namespace Starter.Modules.Lookups.Tests.Application;

public sealed class ActiveLookupValuesTests
{
    private readonly FakeLookupRepository _repository = FakeLookupRepository.Seeded();
    private readonly RecordingLookupCache _cache = new();

    [Fact]
    public async Task An_active_value_is_found_with_its_code_and_names()
    {
        var value = await Port().FindActiveAsync(LookupTypes.ContactTopics, ContactTopicIds.General, CancellationToken.None);

        Assert.Equal(new ActiveLookupValue(ContactTopicIds.General, "general", "general-ar", "general-en"), value);
    }

    [Fact]
    public async Task A_retired_value_an_unknown_id_and_an_unknown_type_are_null()
    {
        _repository.Items.Single(item => item.Id == ContactTopicIds.Account).SetActive(false);
        var port = Port();

        Assert.Null(await port.FindActiveAsync(LookupTypes.ContactTopics, ContactTopicIds.Account, CancellationToken.None));
        Assert.Null(await port.FindActiveAsync(LookupTypes.ContactTopics, id: 999, CancellationToken.None));
        Assert.Null(await port.FindActiveAsync("unknown-type", ContactTopicIds.General, CancellationToken.None));
        Assert.Null(await port.FindActiveAsync(typeKey: null!, ContactTopicIds.General, CancellationToken.None));
    }

    [Fact]
    public async Task Repeated_reads_share_the_cached_active_list()
    {
        var port = Port();

        await port.FindActiveAsync(LookupTypes.ContactTopics, ContactTopicIds.General, CancellationToken.None);
        await port.FindActiveAsync(LookupTypes.ContactTopics, ContactTopicIds.Request, CancellationToken.None);

        Assert.Equal(1, _repository.ItemReads);
    }

    private ActiveLookupValues Port() => new(new GetActiveLookupValues(_repository, _cache));
}
