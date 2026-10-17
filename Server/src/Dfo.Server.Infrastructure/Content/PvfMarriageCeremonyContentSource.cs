using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMarriageCeremonyContentSource(PvfArchiveProvider provider)
    : IMarriageCeremonyContentSource
{
    private readonly Lazy<MarriageCeremonyContent> content = new(() =>
    {
        var document = PvfDocument.Open(provider.Archive, "etc/weddingsystem.etc");
        var values = document.Read(PvfField.Int32List("[ceremony]"));
        if (values.Count == 0 || values.Count % 4 != 0)
            throw new InvalidDataException("Wedding ceremony rules require four values per tier.");
        var gifts = document.Read(PvfField.Int32List("[wedding gift]")).ToArray();
        var tiers = Enumerable.Range(0, values.Count / 4).Select(index =>
        {
            var offset = index * 4;
            return new MarriageCeremonyTier(values[offset], values[offset + 1],
                values[offset + 2], values[offset + 3])
            { GiftItemId = Enumerable.Range(0, gifts.Length / 3).Where(i => gifts[i * 3] == values[offset]).Select(i => gifts[i * 3 + 1]).Single() };
        }).ToArray();
        if (tiers.Select(x => x.Tier).Distinct().Count() != tiers.Length
            || tiers.Any(x => x.Tier < 0 || x.Gold < 0)
            || tiers.All(x => x.Tier != 0))
            throw new InvalidDataException("Wedding ceremony tiers are invalid.");
        var waits = document.Read(PvfField.Int32List("[minute selection]")).ToArray();
        if (waits.Length != 3 || waits.Any(x => x <= 0))
            throw new InvalidDataException("Wedding ceremony requires three positive wait selections.");
        var limits = document.Read(PvfField.Int32List("[money gift limit]"));
        var charge = document.Read(PvfField.Int32List("[money gift charge]"));
        if (limits.Count != 2 || charge.Count != 1 || limits[0] <= 0 || limits[1] < limits[0] || charge[0] is < 0 or > 100)
            throw new InvalidDataException("Invalid wedding gift limits.");
        var rewards = new List<MarriageGuestReward>();
        foreach (var group in document.ReadAll(PvfField.Int32List("[guest gift]")))
        {
            if (group.Count < 4 || (group.Count - 1) % 3 != 0) throw new InvalidDataException("Invalid guest rewards.");
            for (var i = 1; i < group.Count; i += 3) rewards.Add(new(group[0], group[i], group[i + 1], group[i + 2]));
        }
        var cutoff = document.Read(PvfField.Int32List("[noti end]"));
        if (cutoff.Count != 1 || cutoff[0] < 0) throw new InvalidDataException("Invalid invitation cutoff.");
        var force = document.Read(PvfField.Int32List("[force move]"));
        if (force.Count != 6 || force[2] != 0 || force[5] != 0) throw new InvalidDataException("Wedding requires two ground ceremony positions.");
        var standing = Enumerable.Range(0, 2).Select(i => new MarriageCeremonyPosition(checked((short)force[i * 3]), checked((short)force[i * 3 + 1]), force[i * 3 + 2])).ToArray();
        var love = document.Read(PvfField.Int32List("[love point item]"));
        var maximumLove = document.Read(PvfField.Int32("[max love point]", minimum: 1));
        if (love.Count == 0 || love.Count % 2 != 0) throw new InvalidDataException("Invalid love point item rules.");
        var loveItems = Enumerable.Range(0, love.Count / 2).ToDictionary(i => love[i * 2], i => love[i * 2 + 1]);
        if (loveItems.Any(x => x.Key <= 0 || x.Value <= 0)) throw new InvalidDataException("Invalid love point grant.");
        return new MarriageCeremonyContent(tiers, waits)
        { CeremonyPositions = standing, MaximumLovePoints = maximumLove, LovePointItems = loveItems, GiftMinimum = limits[0], GiftMaximum = limits[1], GiftChargePercent = charge[0], GuestRewards = rewards,
            InvitationCutoffSeconds = cutoff[0] };
    });

    public MarriageCeremonyContent Load() => content.Value;
}
