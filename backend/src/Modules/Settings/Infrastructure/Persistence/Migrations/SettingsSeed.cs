using Starter.Application.Settings;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Infrastructure.Persistence.Migrations;

/// <summary>
/// The settings <see cref="InitialCreate"/> inserts. Frozen with the migration: a new setting gets a NEW migration that
/// inserts its row (and a key constant), never an edit here. Every default is checked against its type and range by
/// <see cref="Setting.Define"/> in the module tests.
/// </summary>
public static class SettingsSeed
{
    /// <summary>The <c>session</c> group.</summary>
    public const string SessionGroup = "session";

    /// <summary>The <c>notifications</c> group.</summary>
    public const string NotificationsGroup = "notifications";

    /// <summary>The <c>portal</c> group.</summary>
    public const string PortalGroup = "portal";

    private const int IdleWarningMinutesMin = 1;
    private const int IdleWarningMinutesMax = 240;
    private const int IdleWarningMinutesDefault = 15;
    private const int PageSizeMin = 5;
    private const int PageSizeMax = 100;
    private const int PageSizeDefault = 20;

    /// <summary>The rows, in key order.</summary>
    public static IReadOnlyList<SettingDefinition> Rows { get; } =
    [
        new(
            SettingKeys.NotificationsPageSize,
            NotificationsGroup,
            SettingValueType.WholeNumber,
            SettingValueFormat.Write(PageSizeDefault),
            DescriptionAr: "عدد الإشعارات المعروضة في الصفحة الواحدة من قائمة الإشعارات.",
            DescriptionEn: "How many notifications one page of the notifications feed shows.",
            new SettingRange(PageSizeMin, PageSizeMax)),
        new(
            SettingKeys.PortalContactEnabled,
            PortalGroup,
            SettingValueType.Boolean,
            SettingValueFormat.True,
            DescriptionAr: "هل يقبل نموذج التواصل في البوابة العامة رسائل جديدة.",
            DescriptionEn: "Whether the contact form of the public portal accepts new messages.",
            SettingRange.None),
        new(
            SettingKeys.SessionIdleWarningMinutes,
            SessionGroup,
            SettingValueType.WholeNumber,
            SettingValueFormat.Write(IdleWarningMinutesDefault),
            DescriptionAr: "عدد دقائق عدم النشاط التي يظهر بعدها تنبيه انتهاء الجلسة.",
            DescriptionEn: "Minutes of inactivity after which the session idle warning is shown.",
            new SettingRange(IdleWarningMinutesMin, IdleWarningMinutesMax)),
    ];
}
