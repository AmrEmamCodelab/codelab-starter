using Starter.Application.Settings;
using Starter.Modules.Settings.Domain;
using Starter.Modules.Settings.Tests.Fakes;

namespace Starter.Modules.Settings.Tests.Domain;

public sealed class SettingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Define_starts_at_the_canonical_default_and_keeps_the_range()
    {
        var setting = Setting.Define(new SettingDefinition(
            "portal.contact-enabled", "portal", SettingValueType.Boolean, " TRUE ", "عربي", "English", SettingRange.None));

        Assert.Equal("true", setting.Value);
        Assert.Equal("true", setting.DefaultValue);
        Assert.Null(setting.UpdatedAt);
        Assert.Null(setting.UpdatedByUserId);
        Assert.Equal(SettingRange.None, setting.Range);
        Assert.Equal(new SettingRange(5, 100), SampleSettings.Integer().Range);
    }

    [Fact]
    public void Define_refuses_a_default_that_breaks_its_own_rules() =>
        Assert.Throws<ArgumentException>(() => Setting.Define(new SettingDefinition(
            "notifications.page-size", "notifications", SettingValueType.WholeNumber, "500", "ع", "E", new SettingRange(5, 100))));

    [Fact]
    public void Change_stores_the_canonical_value_and_who_and_when()
    {
        var setting = SampleSettings.Integer();

        Assert.True(setting.Change(" 50 ", Now, byUserId: 7));

        Assert.Equal("50", setting.Value);
        Assert.Equal("20", setting.DefaultValue);
        Assert.Equal(Now, setting.UpdatedAt);
        Assert.Equal(7, setting.UpdatedByUserId);
    }

    [Fact]
    public void Change_to_the_same_canonical_value_changes_nothing()
    {
        var setting = SampleSettings.Integer();

        Assert.False(setting.Change("+20", Now, byUserId: 7));

        Assert.Null(setting.UpdatedAt);
    }

    [Fact]
    public void Change_to_a_refused_value_changes_nothing()
    {
        var setting = SampleSettings.Integer();

        Assert.False(setting.Change("1000", Now, byUserId: 7));

        Assert.Equal("20", setting.Value);
        Assert.Null(setting.UpdatedAt);
    }

    [Fact]
    public void Check_uses_the_setting_type_and_range()
    {
        Assert.Equal(SettingValueProblem.OutOfRange, SampleSettings.Integer().Check("4").Problem);
        Assert.Equal(SettingValueProblem.InvalidFormat, SampleSettings.Boolean().Check("maybe").Problem);
        Assert.True(SampleSettings.Text().Check(string.Empty).IsValid);
    }
}
