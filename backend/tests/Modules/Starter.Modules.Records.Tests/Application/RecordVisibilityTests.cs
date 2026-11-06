using Starter.Domain.Records;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Tests.Support;
using Starter.Workflow.Testing;

namespace Starter.Modules.Records.Tests.Application;

/// <summary>The kernel <see cref="RecordVisibility"/> port: the loader's decision, as record-type modules read it.</summary>
public sealed class RecordVisibilityTests
{
    private readonly RecordsHarness _harness = new();

    private RecordVisibility Visibility => new(_harness.Loader);

    [Fact]
    public async Task The_owner_sees_their_own_draft()
    {
        var reference = await _harness.DraftAsync();

        Assert.True(await Visibility.CanViewAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task A_never_submitted_draft_is_hidden_from_a_queue_holder_and_from_oversight()
    {
        var reference = await _harness.DraftAsync();

        foreach (var user in new[] { _harness.Staff, _harness.Admin })
        {
            _harness.SignIn(user);
            Assert.False(await Visibility.CanViewAsync(reference, CancellationToken.None));
        }
    }

    [Fact]
    public async Task A_submitted_record_is_visible_to_a_queue_holder_and_to_an_oversight_only_admin()
    {
        var reference = await _harness.SubmittedAsync();

        foreach (var user in new[] { _harness.Staff, _harness.Admin })
        {
            _harness.SignIn(user);
            Assert.True(await Visibility.CanViewAsync(reference, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Staff_of_another_organization_and_an_unknown_caller_see_nothing()
    {
        var reference = await _harness.SubmittedAsync();

        _harness.SignIn(_harness.OtherStaff);
        Assert.False(await Visibility.CanViewAsync(reference, CancellationToken.None));
        _harness.SignIn(null);
        Assert.False(await Visibility.CanViewAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_record_or_another_record_type_is_not_visible()
    {
        var reference = await _harness.SubmittedAsync();

        Assert.False(await Visibility.CanViewAsync(new RecordRef(reference.Id + 1000, TestRecordWorkflow.RecordType), CancellationToken.None));
        Assert.False(await Visibility.CanViewAsync(new RecordRef(reference.Id, "other-record"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Visibility.CanViewAsync(null!, CancellationToken.None));
    }
}
