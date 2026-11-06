using Starter.Application.Records;
using Starter.Domain.Records;

namespace Starter.Application.Tests.Records;

/// <summary>RecordRef, the record type key shape and RecordTypeRegistration validation.</summary>
public sealed class RecordKeysAndRegistrationTests
{
    private const string RecordType = "test-record";
    private const string Prefix = "TR";
    private const string QueuePermission = "test-records.queue";
    private const string ViewOwnPermission = "test-records.view-own";
    private const long RecordId = 7;

    [Theory]
    [InlineData("test-record")]
    [InlineData("leave2-request")]
    [InlineData("a")]
    public void A_kebab_record_type_key_is_valid(string key) => Assert.True(RecordTypeKeyFormat.IsValid(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Test-Record")]
    [InlineData("test_record")]
    [InlineData("test--record")]
    [InlineData("-test")]
    [InlineData("test-")]
    [InlineData("2-test")]
    [InlineData("test record")]
    public void A_malformed_record_type_key_is_invalid(string? key) => Assert.False(RecordTypeKeyFormat.IsValid(key));

    [Fact]
    public void A_record_type_key_is_at_most_64_characters()
    {
        Assert.True(RecordTypeKeyFormat.IsValid(new string('a', RecordTypeKeyFormat.MaxLength)));
        Assert.False(RecordTypeKeyFormat.IsValid(new string('a', RecordTypeKeyFormat.MaxLength + 1)));
    }

    [Fact]
    public void A_record_ref_keeps_its_id_and_type()
    {
        var reference = new RecordRef(RecordId, RecordType);

        Assert.Equal((RecordId, RecordType), (reference.Id, reference.RecordType));
        Assert.Equal(new RecordRef(RecordId, RecordType), reference);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_record_ref_below_id_1_is_refused(long id) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordRef(id, RecordType));

    [Theory]
    [InlineData(null)]
    [InlineData("Test")]
    public void A_record_ref_with_a_malformed_type_is_refused(string? recordType) =>
        Assert.Throws<ArgumentException>(() => new RecordRef(RecordId, recordType!));

    [Fact]
    public void A_well_formed_registration_keeps_every_value()
    {
        var registration = Registration();

        Assert.Equal(
            (RecordType, Prefix, QueuePermission, ViewOwnPermission, RecordType),
            (registration.RecordType, registration.NumberPrefix, registration.QueuePermissionKey, registration.ViewOwnPermissionKey, registration.AttachableRecordType));
    }

    [Theory]
    [InlineData("T")]
    [InlineData("tr")]
    [InlineData("TOOLONG")]
    [InlineData("T1")]
    [InlineData("")]
    public void A_registration_refuses_a_prefix_that_is_not_2_to_6_uppercase_letters(string prefix) =>
        Assert.Throws<ArgumentException>(() => Registration(prefix: prefix));

    [Theory]
    [InlineData("Test-Record")]
    [InlineData("")]
    public void A_registration_refuses_a_malformed_record_type(string recordType) =>
        Assert.Throws<ArgumentException>(() => Registration(recordType: recordType));

    [Fact]
    public void A_registration_refuses_a_malformed_attachable_record_type() =>
        Assert.Throws<ArgumentException>(() => Registration(attachableRecordType: "Attach_Me"));

    [Theory]
    [InlineData("queue")]
    [InlineData("Test.Queue")]
    [InlineData("")]
    public void A_registration_refuses_a_malformed_permission_key(string key)
    {
        Assert.Throws<ArgumentException>(() => Registration(queuePermission: key));
        Assert.Throws<ArgumentException>(() => Registration(viewOwnPermission: key));
    }

    private static RecordTypeRegistration Registration(
        string recordType = RecordType,
        string prefix = Prefix,
        string queuePermission = QueuePermission,
        string viewOwnPermission = ViewOwnPermission,
        string attachableRecordType = RecordType) =>
        new(
            RecordType: recordType,
            NumberPrefix: prefix,
            QueuePermissionKey: queuePermission,
            ViewOwnPermissionKey: viewOwnPermission,
            AttachableRecordType: attachableRecordType);
}
