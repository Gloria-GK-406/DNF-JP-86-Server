using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed class HousingReadView(IMarriageRelations ledger, HousingState state, Dfo.Server.Abstractions.Content.IMarriageHousingContentSource content,
    Dfo.Server.Abstractions.Configuration.IDailyResetRuleService days, TimeProvider clock) : IMarriageHousingQueryDomainService
{
    public async ValueTask<MarriageInfoSnapshot?> InfoAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        if (await ledger.ForParticipantAsync(account, character, ct).ConfigureAwait(false) is not { } relation
            || !MarriageStageRules.IsCouple(relation.Stage)) return null;
        var partner = relation.Proposer.CharacterId == character ? relation.Recipient : relation.Proposer;
        return new(partner.CharacterId.Value, relation.Stage, relation.MarriedAt,
            await ledger.PointsAsync(relation.Id, character, ct).ConfigureAwait(false),
            await ledger.RingAsync(relation.Id, ct).ConfigureAwait(false));
    }
    public bool IsEntrySource(byte town, byte area) => content.Load().EntrySources.Contains((town, area));
    public async ValueTask<HousingSnapshot?> ReadAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        if (await ledger.ForParticipantAsync(account, character, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Married } relation) return null;
        var room = await state.ReadAsync(relation.Id, ct).ConfigureAwait(false);
        return room?.Tree is { } tree && tree.WaterPeriod != days.PeriodStart(clock.GetUtcNow()).ToUnixTimeMilliseconds()
            ? room with { Tree = tree with { Waters = 0 } } : room;
    }
    public async ValueTask<int?> InstanceAsync(SelectedSessionIdentity actor, CancellationToken ct) =>
        (await ReadAsync(actor.AccountId, actor.CharacterId, ct).ConfigureAwait(false))?.Instance;
    public async ValueTask<int> PointsAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await ledger.ForParticipantAsync(account, character, ct).ConfigureAwait(false) is { Stage: MarriageStage.Married } relation
            ? await ledger.PointsAsync(relation.Id, character, ct).ConfigureAwait(false) : 0;
    public async ValueTask<byte> RingAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await ledger.ForParticipantAsync(account, character, ct).ConfigureAwait(false) is { Stage: MarriageStage.Married } relation
            ? await ledger.RingAsync(relation.Id, ct).ConfigureAwait(false) : (byte)0;
    public async ValueTask<HousingBenefits> BenefitsAsync(AccountId account, CharacterId character, bool pvp, CancellationToken ct)
    {
        if (await ReadAsync(account, character, ct).ConfigureAwait(false) is not { } room) return new(0, 0, new Dictionary<string, int>());
        var rules = content.Load();
        var value = room.Placements.Sum(x => rules.Furniture.TryGetValue(x.ItemId, out var furniture) && furniture.Slots.Contains(x.Slot) ? furniture.PointCost : 0);
        var ring = await RingAsync(account, character, ct).ConfigureAwait(false);
        var modifiers = new Dictionary<string, int>();
        if (!pvp)
            foreach (var tier in new[] { rules.RoomAttributes.Where(x => x.Threshold <= value).MaxBy(x => x.Threshold), rules.RingAttributes.SingleOrDefault(x => x.Threshold == ring) }.OfType<Dfo.Server.Abstractions.Content.HousingAttributeRule>())
                foreach (var (name, amount) in tier.Values) modifiers[name] = modifiers.GetValueOrDefault(name) + amount;
        return new(value, ring, modifiers);
    }
}
