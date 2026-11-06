using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Tests.Fakes;

namespace Starter.Modules.Lookups.Tests.Application;

public sealed class GetActiveLookupValuesTests
{
    private readonly FakeLookupRepository _repository = FakeLookupRepository.Seeded();
    private readonly RecordingLookupCache _cache = new();

    [Fact]
    public async Task Returns_the_active_values_ordered_by_sort_order()
    {
        _repository.Items.Single(item => item.Id == ContactTopicIds.Account).SetActive(false);

        var outcome = await Use().ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal([ContactTopicIds.General, ContactTopicIds.Request, ContactTopicIds.Suggestion], outcome.Value.Select(value => value.Id));
        var general = outcome.Value[0];
        Assert.Equal(("general", "general-ar", "general-en", 10), (general.Code, general.NameAr, general.NameEn, general.SortOrder));
    }

    [Fact]
    public async Task The_second_read_is_served_from_the_cache()
    {
        var useCase = Use();

        await useCase.ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None);
        var second = await useCase.ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None);

        Assert.Equal(1, _repository.ItemReads);
        Assert.Equal(4, second.Value.Count);
    }

    [Fact]
    public async Task A_type_whose_values_are_all_inactive_answers_an_empty_list()
    {
        var outcome = await Use().ExecuteAsync(FakeLookupRepository.OtherType, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Empty(outcome.Value);
    }

    [Fact]
    public async Task An_unknown_type_is_not_found_and_not_cached()
    {
        var outcome = await Use().ExecuteAsync("unknown-type", CancellationToken.None);

        AssertNotFound(outcome, LookupsErrorCodes.TypeNotFound);
        Assert.Null(_cache.Find("unknown-type"));
        Assert.Equal(0, _repository.ItemReads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Contact-Topics")]
    [InlineData("contact_topics")]
    [InlineData("../users")]
    public async Task A_malformed_type_is_not_found_without_a_query(string? typeKey)
    {
        var outcome = await Use().ExecuteAsync(typeKey, CancellationToken.None);

        AssertNotFound(outcome, LookupsErrorCodes.TypeNotFound);
        Assert.Equal(0, _repository.TypeChecks);
    }

    internal static void AssertNotFound<T>(Outcome<T> outcome, string code)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCategory.NotFound, outcome.Refusal!.Category);
        Assert.Equal(code, outcome.Refusal.Code);
    }

    private GetActiveLookupValues Use() => new(_repository, _cache);
}

public sealed class AdminReadTests
{
    private readonly FakeLookupRepository _repository = FakeLookupRepository.Seeded();

    [Fact]
    public async Task ListLookupTypes_returns_every_type_with_its_value_count_ordered_by_key()
    {
        var outcome = await new ListLookupTypes(_repository).ExecuteAsync(CancellationToken.None);

        Assert.Equal(
            [(LookupTypes.ContactTopics, 4), (FakeLookupRepository.OtherType, 1)],
            outcome.Value.Select(type => (type.Key, type.ItemCount)));
        Assert.Equal(("مواضيع التواصل", "Contact topics"), (outcome.Value[0].NameAr, outcome.Value[0].NameEn));
    }

    [Fact]
    public async Task ListLookupItems_returns_inactive_values_too()
    {
        var outcome = await new ListLookupItems(_repository).ExecuteAsync(FakeLookupRepository.OtherType, CancellationToken.None);

        var item = Assert.Single(outcome.Value);
        Assert.False(item.IsActive);
    }

    [Fact]
    public async Task ListLookupItems_orders_by_sort_order()
    {
        var outcome = await new ListLookupItems(_repository).ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None);

        Assert.Equal(
            [ContactTopicIds.General, ContactTopicIds.Request, ContactTopicIds.Account, ContactTopicIds.Suggestion],
            outcome.Value.Select(item => item.Id));
    }

    [Theory]
    [InlineData("unknown-type")]
    [InlineData("NOT KEBAB")]
    [InlineData(null)]
    public async Task ListLookupItems_answers_an_unknown_or_malformed_type_as_not_found(string? typeKey) =>
        GetActiveLookupValuesTests.AssertNotFound(
            await new ListLookupItems(_repository).ExecuteAsync(typeKey, CancellationToken.None), LookupsErrorCodes.TypeNotFound);
}

public sealed class SetLookupItemActiveTests
{
    private readonly FakeLookupRepository _repository = FakeLookupRepository.Seeded();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingLookupCache _cache = new();

    [Fact]
    public async Task Deactivating_saves_and_invalidates_the_type_cache()
    {
        _cache.Store(LookupTypes.ContactTopics, []);

        var outcome = await Use().ExecuteAsync(LookupTypes.ContactTopics, Id(ContactTopicIds.Request), new(IsActive: false), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.False(_repository.Items.Single(item => item.TypeKey == LookupTypes.ContactTopics && item.Id == ContactTopicIds.Request).IsActive);
        Assert.Equal(1, _unitOfWork.Saves);
        Assert.Equal([LookupTypes.ContactTopics], _cache.Invalidated);
        Assert.Null(_cache.Find(LookupTypes.ContactTopics));
    }

    [Fact]
    public async Task Reactivating_saves_and_invalidates()
    {
        var outcome = await Use().ExecuteAsync(FakeLookupRepository.OtherType, Id(1), new(IsActive: true), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.True(_repository.Items.Single(item => item.TypeKey == FakeLookupRepository.OtherType).IsActive);
        Assert.Equal([FakeLookupRepository.OtherType], _cache.Invalidated);
    }

    [Fact]
    public async Task Repeating_the_current_state_is_a_no_op_success()
    {
        var outcome = await Use().ExecuteAsync(LookupTypes.ContactTopics, Id(ContactTopicIds.General), new(IsActive: true), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(0, _unitOfWork.Saves);
        Assert.Empty(_cache.Invalidated);
    }

    [Fact]
    public async Task A_missing_flag_is_invalid_and_changes_nothing()
    {
        var outcome = await Use().ExecuteAsync(LookupTypes.ContactTopics, Id(ContactTopicIds.General), new(IsActive: null), CancellationToken.None);

        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal!.Category);
        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal.Code);
        Assert.Contains(FieldNames.Of(nameof(SetLookupItemActiveCommand.IsActive)), outcome.Refusal.FieldErrors.Keys);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Theory]
    [InlineData("unknown-type")]
    [InlineData("Bad Type")]
    public async Task An_unknown_type_is_not_found(string typeKey) =>
        GetActiveLookupValuesTests.AssertNotFound(
            await Use().ExecuteAsync(typeKey, Id(1), new(IsActive: false), CancellationToken.None), LookupsErrorCodes.TypeNotFound);

    [Fact]
    public async Task A_value_of_another_type_is_not_found()
    {
        var outcome = await Use().ExecuteAsync(FakeLookupRepository.OtherType, Id(ContactTopicIds.Suggestion), new(IsActive: false), CancellationToken.None);

        GetActiveLookupValuesTests.AssertNotFound(outcome, LookupsErrorCodes.ItemNotFound);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task An_id_beyond_the_int_range_or_absent_is_not_found()
    {
        GetActiveLookupValuesTests.AssertNotFound(
            await Use().ExecuteAsync(LookupTypes.ContactTopics, PublicId<LookupItemIds>.From((long)int.MaxValue + 1), new(IsActive: false), CancellationToken.None),
            LookupsErrorCodes.ItemNotFound);
        GetActiveLookupValuesTests.AssertNotFound(
            await Use().ExecuteAsync(LookupTypes.ContactTopics, default, new(IsActive: false), CancellationToken.None),
            LookupsErrorCodes.ItemNotFound);
    }

    private static PublicId<LookupItemIds> Id(int id) => PublicId<LookupItemIds>.From(id);

    private SetLookupItemActive Use() => new(new SetLookupItemActiveCommandValidator(), _repository, _unitOfWork, _cache);
}

public sealed class SetLookupItemActiveCommandValidatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_present_flag_is_valid(bool isActive) =>
        Assert.True(new SetLookupItemActiveCommandValidator().Validate(new(isActive)).IsValid);

    [Fact]
    public void A_missing_flag_is_required()
    {
        var errors = new SetLookupItemActiveCommandValidator().Validate(new(IsActive: null)).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors[FieldNames.Of(nameof(SetLookupItemActiveCommand.IsActive))]);
    }

    [Fact]
    public void A_null_body_throws() =>
        Assert.Throws<ArgumentNullException>(() => new SetLookupItemActiveCommandValidator().Validate(null!));
}
