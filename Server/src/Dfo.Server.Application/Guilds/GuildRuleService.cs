using System.Text;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRuleService(IGuildEconomySource content)
{
    public const int MaximumDonationGold = 10_000_000;

    public int? CreationCost() => content.ReadGoldCost() is > 0 and var cost ? cost : null;
    public int? DonationGoldPerPoint() => content.ReadDonationGoldPerPoint() is > 0 and var rate ? rate : null;

    public string? NameKey(string name) =>
        name.Length is > 0 and <= 24 && name.Trim() == name && IsWellFormed(name)
            ? name.Normalize().ToUpperInvariant()
            : null;

    public bool IsValidText(string text) => text.Length <= 40 && IsWellFormed(text);

    public bool IsValidPromotion(string text) =>
        IsValidText(text) && StrictUtf8.GetByteCount(text) <= 80;

    public bool IsValidRankName(string text) =>
        text.Length <= 20 && !string.IsNullOrWhiteSpace(text) && IsWellFormed(text)
        && StrictUtf8.GetByteCount(text) <= Dfo.Server.Data.Guilds.GuildRankSettings.MaximumNameBytes;

    public static async ValueTask<GuildId?> FindApplicationReviewGuildAsync(
        IGuildDomainStore store, CharacterId reviewer, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(reviewer, ct).ConfigureAwait(false) is not { } membership)
            return null;
        if (membership.Member.IsLeader)
            return membership.Guild.Id;

        var rank = membership.Member.Rank;
        var ranks = await store.ReadRankSettingsAsync(membership.Guild.Id, ct).ConfigureAwait(false);
        var permissions = ranks.FirstOrDefault(setting => setting.Rank == rank)?.PermissionMask
            ?? GuildRankSettings.Defaults[rank].PermissionMask;
        return (permissions & GuildRankSettings.ReviewApplicationsPermission) != 0 ? membership.Guild.Id : null;
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
