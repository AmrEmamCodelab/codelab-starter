using Starter.Application.Attachments;
using Starter.Domain.Attachments;

namespace Starter.Application.Tests.Attachments;

public sealed class AttachmentPortsTests
{
    [Fact]
    public void The_interlock_code_is_stable()
    {
        Assert.Equal("ATTACHMENTS_REQUIRED_MISSING", AttachmentErrorCodes.RequiredMissing);
    }

    [Fact]
    public void Record_types_and_type_keys_are_kebab_case()
    {
        Assert.All(
            [AttachableRecordTypes.Demo, AttachmentTypeKeys.DemoDocument],
            key => Assert.Matches("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", key));
    }

    [Fact]
    public void The_record_id_label_fits_a_public_id_domain()
    {
        const int maxDomainLength = 32;

        Assert.InRange(AttachableRecordTypes.RecordIdDomainLabel.Length, 1, maxDomainLength);
    }

    [Fact]
    public void Every_type_key_has_an_id_of_the_same_name()
    {
        var ids = Constants(typeof(AttachmentTypeIds));
        var keys = Constants(typeof(AttachmentTypeKeys));

        Assert.Equal(ids.Keys.Order(StringComparer.Ordinal), keys.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ids.Count, ids.Values.Distinct().Count());
        Assert.Equal(keys.Count, keys.Values.Distinct().Count());
    }

    [Fact]
    public async Task A_record_access_answers_per_record()
    {
        IAttachableRecordAccess access = new OnlyRecordOne();

        Assert.True(await access.CanViewAsync(1, CancellationToken.None));
        Assert.False(await access.CanChangeAsync(2, CancellationToken.None));
    }

    private static Dictionary<string, object> Constants(Type type) =>
        type.GetFields().Where(field => field.IsLiteral).ToDictionary(field => field.Name, field => field.GetRawConstantValue()!, StringComparer.Ordinal);

    private sealed class OnlyRecordOne : IAttachableRecordAccess
    {
        public string RecordType => AttachableRecordTypes.Demo;

        public Task<bool> CanViewAsync(long recordId, CancellationToken cancellationToken) => Task.FromResult(recordId == 1);

        public Task<bool> CanChangeAsync(long recordId, CancellationToken cancellationToken) => Task.FromResult(recordId == 1);
    }
}
