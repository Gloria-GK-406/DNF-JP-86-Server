using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>All durable effects of a participant's verified kill, inside the caller's transaction.</summary>
public sealed class MonsterKillRewardsDomainService(ICharacterDomainService characters,
    ICharacterQuestLogDomainService logs, ICharacterContainerDomainService containers,
    IQuestProgressDomainService quests, IItemDeliveryDomainService delivery,
    IPartyService? parties = null, TimeProvider? clock = null, ICharacterGameplayFactDomainService? gameplay = null)
    : IMonsterKillRewardsDomainService
{
    public async ValueTask<MonsterKillEffects> ApplyAsync(SelectedSessionIdentity? session,
        AccountId accountId, CharacterId characterId, IdentifiedKill kill,
        int dungeonId, DungeonDifficulty difficulty, double weight, int partyMemberCount,
        PartyKey partyKey, bool finalBoss, DungeonRunId? runId, RoomActorId? actor,
        int? worldMapId, IReadOnlyList<RolledDrop>? loot, CancellationToken ct)
    {
        var award = kill.IsMonster ? await characters.Get(characterId).AddExpAsync(
            new KillExperienceContext(new ExperienceOccasion(ExperienceEvent.MonsterKill, accountId, characterId, dungeonId),
                kill.Level, kill.Rank, difficulty, weight, partyMemberCount, kill.IsNamedMonster),
            (clock ?? TimeProvider.System).GetUtcNow(), session, ct).ConfigureAwait(false) : null;
        var grants = await GrantQuestItemsAsync(accountId, characterId, loot, partyKey, ct).ConfigureAwait(false);
        var facts = new List<QuestProgressFact> { new QuestEnemyDefeated(dungeonId, difficulty, kill.Code,
            kill.IsMonster ? HuntEnemyTargetTypes.Monster : HuntEnemyTargetTypes.AiCharacter, kill.Rank,
            WorldMapId: worldMapId, RunId: runId, Actor: actor) };
        if (worldMapId is null && finalBoss && kill.IsMonster && kill.Rank == MonsterRank.Boss)
            facts.Add(new QuestDungeonCleared(dungeonId, difficulty));
        if (grants.QuestItems is not null || grants.AccountMaterials.Count > 0) facts.Add(new QuestHoldingsChanged());
        var report = await quests.ReportAsync(accountId, characterId, facts, ct).ConfigureAwait(false);
        if (gameplay is not null && kill.IsMonster)
        {
            await gameplay.RecordMonsterDefeatedAsync(new(characterId, kill.Code, (int)kill.Rank, dungeonId, (int)difficulty), ct).ConfigureAwait(false);
            if (finalBoss && kill.Rank == MonsterRank.Boss)
                await gameplay.RecordDungeonCompletedAsync(new(characterId, dungeonId, (int)difficulty), ct).ConfigureAwait(false);
        }
        return grants with { Award = award, QuestProgress = report.NotificationSnapshot, QuestChanges = report.Changes };
    }

    private async ValueTask<MonsterKillEffects> GrantQuestItemsAsync(AccountId account, CharacterId character,
        IReadOnlyList<RolledDrop>? loot, PartyKey party, CancellationToken ct)
    {
        var empty = new MonsterKillEffects(null, null, null, []);
        if (party != default && (parties?.FindByKey(party) is not { } current
            || !current.Members.Any(x => x.CharacterId == character))) return empty;
        var candidates = loot?.Where(x => x.Delivery == RolledDropDelivery.DirectQuestItem
            && x.OwnerCharacterId == character).ToArray() ?? [];
        if (candidates.Length == 0) return empty;
        var active = (await logs.LogOf(account, character).CarriedAsync(ct).ConfigureAwait(false)).Select(x => x.QuestId).ToHashSet();
        var changed = new Dictionary<GameSlot, (IBusinessItemContainer Container, int Index)>();
        var slots = new InventorySlotMappingRuleService();
        foreach (var drop in candidates)
        {
            if (drop.QuestId <= 0 || !active.Contains(drop.QuestId) || drop.ItemId <= 0 || drop.Count <= 0 || drop.GrantLimit <= 0) continue;
            var bag = containers.FindContainer(character, AccountMaterialCatalog.ContainsItem(drop.ItemId)
                ? InventoryContainerType.SharedMaterial : InventoryContainerType.Main)
                ?? throw new InvalidDataException("Quest inventory is unavailable.");
            var held = (await bag.GetAllAsync(ct).ConfigureAwait(false)).Where(x => x.ItemId == drop.ItemId).Sum(x => (long)x.Count)
                + await delivery.PendingQuantityAsync(character, drop.ItemId, ct).ConfigureAwait(false);
            var count = checked((int)Math.Min(drop.Count, Math.Max(0, drop.GrantLimit - held)));
            if (count <= 0) continue;
            var receipt = await delivery.DeliverAsync(account, character, [new(drop.ItemId, count)], "任务掉落奖励", ct,
                ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
            foreach (var addition in receipt.Inventory) changed[slots.ToGameSlot(addition.Slot)] = (addition.Container, addition.Slot.Index);
        }
        var items = new List<InventoryItemState>();
        var shared = new List<InventoryRuntimeValueState>();
        foreach (var (slot, source) in changed.OrderBy(x => x.Key.Index))
        {
            var item = await source.Container.GetAsync(source.Index, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Granted quest item disappeared before commit.");
            if (source.Container.Space == BusinessInventorySpace.SharedMaterial) shared.Add(new(slot.Index, item.ItemId, item.Count));
            else items.Add(new(null, slot.Index, item.Snapshot.ToCore()));
        }
        return empty with { QuestItems = items.Count == 0 ? null : new(InventoryContainerType.Main, items, (clock ?? TimeProvider.System).GetUtcNow()), AccountMaterials = shared };
    }
}
