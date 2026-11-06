using Starter.Application.Attachments;
using Starter.Application.Results;
using Starter.Domain.Attachments;

namespace Starter.Application.Tests.Attachments;

public sealed class RequiredAttachmentsGateTests
{
    private const long RecordId = 7;

    [Fact]
    public async Task A_record_missing_a_required_type_is_refused_with_409_required_missing()
    {
        var interlock = new FixedInterlock([AttachmentTypeKeys.DemoDocument]);

        var refusal = await interlock.RefuseWhileMissingAsync(AttachableRecordTypes.Demo, RecordId, CancellationToken.None);

        Assert.Equal((RefusalCategory.Conflict, AttachmentErrorCodes.RequiredMissing), (refusal!.Category, refusal.Code));
        Assert.Equal((AttachableRecordTypes.Demo, RecordId), interlock.Asked);
    }

    [Fact]
    public async Task A_record_with_every_required_type_goes_ahead()
    {
        var refusal = await new FixedInterlock([]).RefuseWhileMissingAsync(AttachableRecordTypes.Demo, RecordId, CancellationToken.None);

        Assert.Null(refusal);
    }

    [Fact]
    public async Task A_missing_interlock_is_a_programming_error()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RequiredAttachmentsGate.RefuseWhileMissingAsync(null!, AttachableRecordTypes.Demo, RecordId, CancellationToken.None));
    }

    private sealed class FixedInterlock(IReadOnlyList<string> missing) : IRequiredAttachments
    {
        public (string RecordType, long RecordId) Asked { get; private set; }

        public Task<IReadOnlyList<string>> FindMissingTypeKeysAsync(string recordType, long recordId, CancellationToken cancellationToken)
        {
            Asked = (recordType, recordId);
            return Task.FromResult(missing);
        }
    }
}
