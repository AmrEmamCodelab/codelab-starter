using System.Globalization;
using System.Net;
using Starter.Application.Settings;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Settings;

/// <summary>
/// Exit criterion: a settings change made through the admin API on node A is visible on node B (a second host on the
/// same database, with its own snapshot cache and a <c>FakeTimeProvider</c>) within 30 seconds.
/// </summary>
[Collection(Phase5HostGroup.Name)]
public sealed class TwoHostSettingsTests(Phase5Host host)
{
    private const string AdminSettings = "/api/admin/settings/";
    private const string ValueMember = "value";

    /// <summary>The setting the tests change (integer, 1 to 240), restored afterwards.</summary>
    private const string Key = SettingKeys.SessionIdleWarningMinutes;

    /// <summary>Two values inside the setting's range; the test writes whichever differs from the current one.</summary>
    private const int FirstProbeValue = 37;
    private const int SecondProbeValue = 38;

    [Fact]
    public async Task Without_the_per_request_stamp_check_node_B_sees_the_change_exactly_when_its_30_second_snapshot_expires()
    {
        await using var nodeB = SecondNode.Start(host, checkStampPerScope: false);
        var before = nodeB.ReadInt32(Key); // loads node B's snapshot at its clock's t0
        var changed = ProbeValueOtherThan(before);
        var adminToken = await host.AdminTokenAsync();
        using var api = host.CreateApi();
        try
        {
            await PutAsync(api, changed, adminToken);

            Assert.Equal(before, nodeB.ReadInt32(Key)); // no stamp check on B's request path, snapshot younger than the TTL
            nodeB.Clock.Advance(SettingsLimits.CacheTtl - TimeSpan.FromTicks(1));
            Assert.Equal(before, nodeB.ReadInt32(Key));
            nodeB.Clock.Advance(TimeSpan.FromTicks(1)); // exactly 30 s after the load: the bound
            Assert.Equal(changed, nodeB.ReadInt32(Key));
        }
        finally
        {
            await PutAsync(api, before, adminToken);
        }
    }

    [Fact]
    public async Task With_the_default_per_request_stamp_check_node_B_sees_the_change_on_its_next_request()
    {
        await using var nodeB = SecondNode.Start(host, checkStampPerScope: true);
        var before = nodeB.ReadInt32(Key);
        var changed = ProbeValueOtherThan(before);
        var adminToken = await host.AdminTokenAsync();
        using var api = host.CreateApi();
        try
        {
            await PutAsync(api, changed, adminToken);

            Assert.Equal(changed, nodeB.ReadInt32(Key)); // the clock did not move: the stamp check alone reloads
        }
        finally
        {
            await PutAsync(api, before, adminToken);
        }
    }

    private static int ProbeValueOtherThan(int current) => current == FirstProbeValue ? SecondProbeValue : FirstProbeValue;

    private static async Task PutAsync(ApiClient api, int value, string adminToken)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        using var response = await api.PutAsync(AdminSettings + Key, new { value = text }, adminToken);
        Assert.Equal(text, (await ApiClient.ReadAsync(response, HttpStatusCode.OK)).GetProperty(ValueMember).GetString());
    }
}
