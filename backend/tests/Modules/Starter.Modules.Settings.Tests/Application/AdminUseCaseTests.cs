using Microsoft.Extensions.Time.Testing;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Settings.Application;
using Starter.Modules.Settings.Application.Admin;
using Starter.Modules.Settings.Application.Reading;
using Starter.Modules.Settings.Domain;
using Starter.Modules.Settings.Tests.Fakes;

namespace Starter.Modules.Settings.Tests.Application;

public sealed class UpdateSettingRequestValidatorTests
{
    private readonly UpdateSettingRequestValidator _validator = new();

    [Fact]
    public void The_value_field_has_its_wire_name() => Assert.Equal("value", UpdateSettingRequestValidator.ValueField);

    [Fact]
    public void A_missing_value_is_required() =>
        Assert.Equal([ValidationMessages.Required], _validator.Validate(new UpdateSettingRequest(Value: null)).ToDictionary()["value"]);

    [Fact]
    public void An_empty_value_passes_the_shape_check_the_type_decides() =>
        Assert.True(_validator.Validate(new UpdateSettingRequest(string.Empty)).IsValid);

    [Fact]
    public void A_value_longer_than_the_column_is_too_long() =>
        Assert.Equal(
            [ValidationMessages.TooLong],
            _validator.Validate(new UpdateSettingRequest(new string('x', SettingFieldLimits.ValueMaxLength + 1))).ToDictionary()["value"]);
}

public sealed class ListSettingsTests
{
    [Fact]
    public async Task It_lists_every_setting_by_group_then_key()
    {
        var useCase = new ListSettings(new FakeSettingsRepository([.. SampleSettings.All()]));

        var outcome = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(
            [SampleSettings.IntegerKey, SampleSettings.DurationKey, SampleSettings.TextKey, SampleSettings.BooleanKey],
            outcome.Value.Select(setting => setting.Key));
    }
}

public sealed class UpdateSettingTests
{
    private static readonly Guid AdminSubject = Guid.Parse("6f1d7e2a-0000-4000-8000-000000000001");
    private const long AdminUserId = 42;

    private readonly FakeSettingsRepository _repository = new([.. SampleSettings.All()]);
    private readonly FakeSettingsUnitOfWork _unitOfWork = new();
    private readonly SettingsSnapshotCache _cache = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_valid_value_is_stored_canonical_with_the_actor_and_advances_the_stamp()
    {
        _cache.Offer(SettingsSnapshot.From(new StoredSettings(1, new Dictionary<string, StoredSetting>()), _clock.GetUtcNow()));

        var outcome = await UseCase().ExecuteAsync(SampleSettings.IntegerKey, new UpdateSettingRequest(" 50 "), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("50", outcome.Value.Value);
        Assert.Equal(_clock.GetUtcNow(), outcome.Value.UpdatedAt);
        Assert.Equal(AdminUserId, outcome.Value.UpdatedByUserId);
        Assert.Equal(1, _unitOfWork.StampAdvances);
        Assert.Null(_cache.Current);
    }

    [Fact]
    public async Task An_unchanged_value_writes_nothing()
    {
        var outcome = await UseCase().ExecuteAsync(SampleSettings.BooleanKey, new UpdateSettingRequest("TRUE"), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Null(outcome.Value.UpdatedAt);
        Assert.Equal(0, _unitOfWork.StampAdvances);
    }

    [Theory]
    [InlineData(SampleSettings.IntegerKey, "abc", ValidationMessages.InvalidFormat)]
    [InlineData(SampleSettings.IntegerKey, "1000", SettingsErrorCodes.ValueOutOfRange)]
    [InlineData(SampleSettings.BooleanKey, "yes", ValidationMessages.InvalidFormat)]
    [InlineData(SampleSettings.DurationKey, "-5", ValidationMessages.InvalidFormat)]
    [InlineData(SampleSettings.DurationKey, "5", SettingsErrorCodes.ValueOutOfRange)]
    [InlineData(SampleSettings.IntegerKey, null, ValidationMessages.Required)]
    public async Task A_value_that_breaks_the_type_or_range_is_a_400_on_the_value_field(string key, string? value, string message)
    {
        var outcome = await UseCase().ExecuteAsync(key, new UpdateSettingRequest(value), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal.Category);
        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal.Code);
        Assert.Equal([message], outcome.Refusal.FieldErrors[UpdateSettingRequestValidator.ValueField]);
        Assert.Equal(0, _unitOfWork.StampAdvances);
    }

    [Theory]
    [InlineData("nope.missing")]
    [InlineData("NOTIFICATIONS.PAGE-SIZE")]
    [InlineData(" ")]
    public async Task An_unknown_key_is_a_404(string key)
    {
        var outcome = await UseCase().ExecuteAsync(key, new UpdateSettingRequest("1"), CancellationToken.None);

        Assert.Equal(RefusalCategory.NotFound, outcome.Refusal!.Category);
        Assert.Equal(SettingsErrorCodes.SettingNotFound, outcome.Refusal.Code);
    }

    [Fact]
    public async Task An_unknown_caller_is_recorded_as_no_user()
    {
        var useCase = UseCase(new SettingChangeMarks(new FakeCurrentUser(Guid.NewGuid()), new FakeUserDirectory(), _clock));

        var outcome = await useCase.ExecuteAsync(SampleSettings.TextKey, new UpdateSettingRequest("Hello"), CancellationToken.None);

        Assert.Null(outcome.Value.UpdatedByUserId);
        Assert.Equal(1, _unitOfWork.StampAdvances);
    }

    [Fact]
    public void Every_refusal_reason_has_a_field_message_and_none_is_not_one()
    {
        Assert.Equal(ValidationMessages.InvalidFormat, UpdateSetting.MessageOf(SettingValueProblem.InvalidFormat));
        Assert.Equal(SettingsErrorCodes.ValueOutOfRange, UpdateSetting.MessageOf(SettingValueProblem.OutOfRange));
        Assert.Equal(ValidationMessages.TooLong, UpdateSetting.MessageOf(SettingValueProblem.TooLong));
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdateSetting.MessageOf(SettingValueProblem.None));
    }

    private UpdateSetting UseCase(SettingChangeMarks? marks = null) => new(
        new UpdateSettingRequestValidator(),
        _repository,
        _unitOfWork,
        _cache,
        marks ?? new SettingChangeMarks(new FakeCurrentUser(AdminSubject), new FakeUserDirectory().Add(AdminSubject, AdminUserId), _clock));
}

public sealed class SettingChangeMarksTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task An_anonymous_mark_has_no_user()
    {
        var mark = await new SettingChangeMarks(new FakeCurrentUser(subjectId: null), new FakeUserDirectory(), _clock).CurrentAsync(CancellationToken.None);

        Assert.Equal(new SettingChangeMark(_clock.GetUtcNow(), ByUserId: null), mark);
    }

    [Fact]
    public async Task A_known_caller_is_resolved_to_the_internal_user_id()
    {
        var subject = Guid.NewGuid();

        var mark = await new SettingChangeMarks(new FakeCurrentUser(subject), new FakeUserDirectory().Add(subject, userId: 9), _clock).CurrentAsync(CancellationToken.None);

        Assert.Equal(new SettingChangeMark(_clock.GetUtcNow(), ByUserId: 9), mark);
    }
}
