namespace LinkDeck.Core.Config;

public enum NoticeLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>A user-facing message produced while loading or saving (shown as a notification).</summary>
public sealed record ConfigNotice(NoticeLevel Level, string Message);
