using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed class AntonRaidRewardProjection(IAntonRaidRewardSettlementDomainService settlements,
    IAntonRaidDomainService raids, ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views, IGameCommandOutbox outbox, TimeProvider clock)
{
    internal async ValueTask ReplayAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (raids.SettlementOf(session) is not { } phase || settlements.Find(phase.Instance, phase.Phase) is not { } snapshot
            || snapshot.Stage == AntonRaidRewardStage.Complete)
            return;

        var eligible = snapshot.Seed.Succeeded && snapshot.Seed.Eligible.Any(member => member.Character == session.CharacterId);
        var result = AntonRaidRewardNotice.Result(snapshot, eligible);
        if (snapshot.Stage == AntonRaidRewardStage.Result)
            await SendAsync(session, [result, AntonRaidRewardNotice.State(3, 0)], ct).ConfigureAwait(false);
        else
        {
            await SendAsync(session, [result], ct).ConfigureAwait(false);
            if (snapshot.Seed.Succeeded && snapshot.Seed.Eligible.Count > 0)
                await OpenAsync(snapshot, session, ct).ConfigureAwait(false);
        }
    }

    internal SelectedSessionIdentity? Online(AntonRaidRewardSnapshot snapshot, CharacterId character) =>
        sessions.TryGetInWorld(character, out var session)
            && raids.SettlementOf(session) == (snapshot.Seed.Instance, snapshot.Seed.Phase)
            ? session : null;

    internal async ValueTask PromptAsync(AntonRaidRewardSnapshot snapshot, CancellationToken ct)
    {
        foreach (var recipient in Audience(snapshot))
            await SendAsync(recipient, [AntonRaidRewardNotice.MoviePrompt()], ct).ConfigureAwait(false);
    }

    internal async ValueTask OpenAsync(AntonRaidRewardSnapshot snapshot, SelectedSessionIdentity? only, CancellationToken ct)
    {
        foreach (var recipient in Audience(snapshot))
        {
            if (only is not null && only != recipient)
                continue;
            var own = snapshot.Seed.Eligible.SingleOrDefault(member => member.Character == recipient.CharacterId);
            var shown = own is null ? [] : snapshot.Revealed.Where(card => SameSquad(snapshot, own, card.Character)).ToArray();
            await CardsAsync(snapshot, recipient, shown, allKinds: true,
                prefix: AntonRaidRewardNotice.State(4, own is null ? (byte)1 : (byte)0), ct).ConfigureAwait(false);
        }
    }

    internal async ValueTask RevealAsync(AntonRaidRewardSnapshot snapshot,
        IReadOnlyList<AntonRaidRewardCard> cards, bool allRaid, CancellationToken ct)
    {
        foreach (var recipient in Audience(snapshot))
        {
            var own = snapshot.Seed.Eligible.SingleOrDefault(member => member.Character == recipient.CharacterId);
            var shown = allRaid ? cards : own is null ? [] : cards.Where(card => SameSquad(snapshot, own, card.Character)).ToArray();
            if (shown.Count > 0)
                await CardsAsync(snapshot, recipient, shown, allKinds: false, prefix: null, ct).ConfigureAwait(false);
        }
    }

    internal async ValueTask AssetsAsync(SelectedSessionIdentity session, AntonRaidRewardReceipt receipt, CancellationToken ct)
    {
        if (!receipt.Claimed || !sessions.IsInWorld(session))
            return;
        var frames = new List<IGameServerMessage>();
        var now = clock.GetUtcNow();
        if (receipt.Gold is { } gold)
            frames.Add(GameInventoryDelivery.CarriedGoldNotification(gold, now));

        if (receipt.Delivery is { } delivery)
        {
            foreach (var group in delivery.Inventory.GroupBy(item => A21InventoryLayout.ToGameSlot(item.Slot).Space))
                frames.Add(GameInventoryDelivery.Notification(new(group.Key,
                    group.GroupBy(item => item.Slot).Select(items =>
                    {
                        var last = items.Last();
                        return new InventoryItemState(null, A21InventoryLayout.ToGameSlot(last.Slot).Index, last.Snapshot.ToCore());
                    }).ToArray(), now)));
        }

        if (receipt.Quests is { } quests)
            frames.Add(new AcceptedQuestProgressMessage(quests.Quests.OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))
                .ToArray()));
        if (frames.Count != 0)
            await SendAsync(session, frames, ct).ConfigureAwait(false);
    }

    private async ValueTask CardsAsync(AntonRaidRewardSnapshot snapshot, SelectedSessionIdentity recipient,
        IReadOnlyList<AntonRaidRewardCard> cards, bool allKinds, AntonRaidRewardNotice? prefix, CancellationToken ct)
    {
        var frames = new List<IGameServerMessage>();
        if (prefix is not null)
            frames.Add(prefix);

        var users = new Dictionary<CharacterId, ushort>();
        foreach (var character in cards.Select(card => card.Character).Distinct())
        {
            if (Online(snapshot, character) is not { } player)
                continue;
            if (player != recipient)
            {
                if (await views.TryLoadAsync(player, ct).ConfigureAwait(false) is not { } view)
                    continue;
                frames.Add(AntonRaidRecruitmentProjection.ProfileOf(view));
            }
            users.Add(character, player.WireUserId);
        }

        var kinds = allKinds
            ? new[] { AntonRaidRewardKind.Gold, AntonRaidRewardKind.PartyCard }
            : cards.Select(card => card.Kind).Distinct();
        foreach (var kind in kinds)
            frames.Add(AntonRaidRewardNotice.Cards(kind, cards.Where(card => card.Kind == kind && users.ContainsKey(card.Character))
                .Select(card => (users[card.Character], card)).ToArray()));
        if (Online(snapshot, recipient.CharacterId) == recipient)
            await SendAsync(recipient, frames, ct).ConfigureAwait(false);
    }

    private IEnumerable<SelectedSessionIdentity> Audience(AntonRaidRewardSnapshot snapshot) =>
        snapshot.Seed.Audience.Concat(snapshot.Seed.Eligible.Select(member => member.Character)).Distinct()
            .Select(character => Online(snapshot, character)).OfType<SelectedSessionIdentity>();

    private static bool SameSquad(AntonRaidRewardSnapshot snapshot, AntonRaidRewardParticipant own, CharacterId other) =>
        other == own.Character || own.Squad != 0
            && snapshot.Seed.Eligible.Any(member => member.Character == other && member.Squad == own.Squad);

    private ValueTask SendAsync(SelectedSessionIdentity session, IReadOnlyList<IGameServerMessage> frames, CancellationToken ct) =>
        outbox.RegisterExplicitAsync(session.ConnectionId, new(frames, []), ct);
}
