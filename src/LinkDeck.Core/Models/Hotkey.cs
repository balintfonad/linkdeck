namespace LinkDeck.Core.Models;

/// <summary>
/// The launcher gesture, Alt+Tab style: press <see cref="Hold"/> and <see cref="Step"/> together to open,
/// keep holding <see cref="Hold"/> and tap <see cref="Step"/> to move on, release <see cref="Hold"/> to open the selection.
/// Default: hold Alt, step with Ctrl.
/// </summary>
public sealed record Hotkey
{
    /// <summary>Modifiers usable as hold or step key. "Alt" means the left Alt key (AltGr is excluded).</summary>
    public static readonly IReadOnlyList<string> KnownModifiers = ["Ctrl", "Alt", "Win"];

    /// <summary>A fresh default each time (the type is settable for JSON, so no shared instance).</summary>
    public static Hotkey Default => new() { Hold = "Alt", Step = "Ctrl" };

    public string Hold { get; set; } = "Alt";

    public string Step { get; set; } = "Ctrl";

    /// <summary>Both are known modifiers and they differ.</summary>
    public bool IsValid() =>
        IsKnown(Hold) && IsKnown(Step) && !string.Equals(Hold, Step, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Hold}+{Step}";

    private static bool IsKnown(string? m) => m is not null && KnownModifiers.Contains(m, StringComparer.OrdinalIgnoreCase);
}
