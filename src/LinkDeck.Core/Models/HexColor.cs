namespace LinkDeck.Core.Models;

/// <summary><c>#RRGGBB</c> colour strings used by groups and items.</summary>
public static class HexColor
{
    public static bool IsValid(string? s) =>
        s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(char.IsAsciiHexDigit);

    /// <summary>Returns a valid colour in upper case, or null for empty/invalid input.</summary>
    public static string? Normalize(string? s)
    {
        s = s?.Trim();
        return IsValid(s) ? s!.ToUpperInvariant() : null;
    }
}
