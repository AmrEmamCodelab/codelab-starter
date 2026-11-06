namespace Starter.Modules.Notifications.Domain;

/// <summary>The Notifications module's sizes (columns, checks and the feed use the same constants).</summary>
public static class NotificationsLimits
{
    /// <summary>The longest template key.</summary>
    public const int TemplateKeyMaxLength = 64;

    /// <summary>The longest template subject (an e-mail subject or a bell title).</summary>
    public const int SubjectMaxLength = 256;

    /// <summary>The longest rendered bell title.</summary>
    public const int TitleTextMaxLength = 512;

    /// <summary>The longest rendered bell body.</summary>
    public const int BodyTextMaxLength = 4000;

    /// <summary>The longest bell link.</summary>
    public const int LinkMaxLength = 512;

    /// <summary>The longest e-mail address.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>The largest feed page a caller may ask for.</summary>
    public const int MaxFeedPageSize = 50;
}
