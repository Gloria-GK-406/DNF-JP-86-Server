using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfPvpColosseumRuleSource(PvfArchiveProvider provider) : IPvpColosseumRuleSource
{
    private const string Path = "etc/pvpcolosseum.etc";
    private readonly Lazy<PvpColosseumRules> data = new(() => Read(provider.Archive));

    public PvpColosseumRules Load() => data.Value;

    private static PvpColosseumRules Read(PvfArchive archive)
    {
        var script = PvfDocument.Open(archive, Path).AtRoot();
        var entrance = script.Read(PvfField.Int32List("[entrance info]"));
        var limits = script.Read(PvfField.Int32List("[daily purchase limit]"));
        var finish = script.Read(PvfField.Int32List("[finish condition]"));
        var minimumRp = script.Read(PvfField.Int32("[minimum entrance rp]", minimum: 0));
        PvfField.Require(entrance is [1, 2, > 0] && limits is [> 0, > 0] && finish is [> 0, > 0],
            Path, "[entrance info]", "expected the A21 emblem payment, positive limits and finish conditions");

        var entries = script.Read(PvfField.Int32List("[reward]"));
        var rewards = new Dictionary<int, IReadOnlyList<PvpColosseumRewardOption>>();
        for (var offset = 0; offset < entries.Count;)
        {
            PvfField.Require(entries.Count - offset >= 3, Path, "[reward]", "missing reward row header");
            var wins = entries[offset++];
            var groups = entries[offset++];
            var count = entries[offset++];
            PvfField.Require(wins >= 0 && wins <= finish[0] && groups > 0
                && count >= groups && count <= (entries.Count - offset) / 5,
                Path, "[reward]", "invalid reward row header");

            var options = new List<PvpColosseumRewardOption>(count);
            for (var n = 0; n < count; n++)
            {
                var group = entries[offset++];
                var item = entries[offset++];
                var quantity = entries[offset++];
                var weight = entries[offset++];
                var type = entries[offset++];
                PvfField.Require(group >= 0 && group < groups && item > 0 && quantity > 0
                    && weight > 0 && type is >= 0 and <= 3, Path, "[reward]", "invalid reward option");
                options.Add(new(group, weight, new(item, quantity, type)));
            }
            PvfField.Require(options.GroupBy(option => option.Group).Count() == groups && rewards.TryAdd(wins, options),
                Path, "[reward]", "missing reward group or duplicate row");
        }
        PvfField.Require(rewards.Count == finish[0] + 1, Path, "[reward]", "missing reward for a win count");
        return new(entrance[1], entrance[2], limits[0], finish[0], finish[1], minimumRp, rewards);
    }
}
