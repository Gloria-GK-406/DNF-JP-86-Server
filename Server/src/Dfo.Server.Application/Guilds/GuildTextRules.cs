using System.Text;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal static class GuildTextRules
{
    private const int MaximumBoardPostWidth = 120;

    public static string? NameKey(string name) =>
        name.Length is > 0 and <= 24 && name.Trim() == name && IsWellFormed(name)
            ? name.Normalize().ToUpperInvariant()
            : null;

    public static bool IsValidText(string text) => text.Length <= 40 && IsWellFormed(text);

    public static bool IsValidPromotion(string text) =>
        IsValidText(text) && StrictUtf8.GetByteCount(text) <= 80;

    public static bool IsValidAnnouncement(string text) =>
        text.Length <= 60 && IsWellFormed(text) && StrictUtf8.GetByteCount(text) <= 120;

    public static bool IsValidMemberMemo(string text) =>
        text.Length <= GuildMember.MaximumMemoCharacters && IsWellFormed(text)
        && StrictUtf8.GetByteCount(text) <= GuildMember.MaximumMemoBytes;

    public static bool IsValidRankName(string text) =>
        text.Length <= 20 && !string.IsNullOrWhiteSpace(text) && IsWellFormed(text)
        && StrictUtf8.GetByteCount(text) <= GuildRankSettings.MaximumNameBytes;

    public static bool IsValidBoardPost(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += rune.IsAscii ? 1 : 2;
            if (Rune.IsControl(rune) || width > MaximumBoardPostWidth)
                return false;
        }
        return true;
    }

    private static bool IsWellFormed(string text)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(text);
            return !text.Any(char.IsControl);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
}
