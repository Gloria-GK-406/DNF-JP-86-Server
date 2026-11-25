using System.Text;
using Dfo.Server.Abstractions.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRuleService(IGuildCreationCostSource content)
{
    public int? CreationCost() => content.ReadGoldCost() is > 0 and var cost ? cost : null;

    public string? NameKey(string name) =>
        name.Length is > 0 and <= 24 && name.Trim() == name && IsWellFormed(name)
            ? name.Normalize().ToUpperInvariant()
            : null;

    public bool IsValidText(string text) => text.Length <= 40 && IsWellFormed(text);

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
