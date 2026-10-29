using Dfo.Server.Abstractions.Effects;
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
    IQuestItemDropDomainService questItems, IQuestProgressDomainService quests,
    IPartyService? parties = null, TimeProvider? clock = null, ICharacterGameplayFactDomainService? gameplay = null)
    : IMonsterKillRewardsDomainService
{
    public async ValueTask<MonsterKillEffects> ApplyAsync(SelectedSessionIdentity? session,
        AccountId accountId, CharacterId characterId, IdentifiedKill kill,
        int dungeonId, DungeonDifficulty difficulty, double weight, int partyMemberCount,
        PartyKey partyKey, bool finalBoss, DungeonRunId? runId, RoomActorId? actor,
        int? worldMapId, IReadOnlyList<RolledDrop>? loot, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(accountId, characterId, session));
        var award = kill.IsMonster ? await characters.Get(characterId).AddExpAsync(
            new KillExperienceContext(new ExperienceOccasion(ExperienceEvent.MonsterKill, accountId, characterId, dungeonId),
                kill.Level, kill.Rank, difficulty, weight, partyMemberCount, kill.IsNamedMonster),
            (clock ?? TimeProvider.System).GetUtcNow(), session, footprint, ct).ConfigureAwait(false) : null;
        footprint.DemandEffects(new(accountId, characterId, session));
        var grants = await GrantQuestItemsAsync(session, accountId, characterId, loot, partyKey, footprint, ct).ConfigureAwait(false);
        var facts = new List<QuestProgressFact> { new QuestEnemyDefeated(dungeonId, difficulty, kill.Code,
            kill.IsMonster ? HuntEnemyTargetTypes.Monster : HuntEnemyTargetTypes.AiCharacter, kill.Rank,
            WorldMapId: worldMapId, RunId: runId, Actor: actor) };
        if (worldMapId is null && finalBoss && kill.IsMonster && kill.Rank == MonsterRank.Boss)
            facts.Add(new QuestDungeonCleared(dungeonId, difficulty));
        if (grants.QuestItems is not null || grants.AccountMaterials.Count > 0) facts.Add(new QuestHoldingsChanged());
        footprint.DemandEffects(new(accountId, characterId, session));
        var report = await quests.ReportAsync(accountId, characterId, facts, ct).ConfigureAwait(false);
        if (gameplay is not null && kill.IsMonster)
        {
            footprint.DemandEffects(new(accountId, characterId, session));
            await gameplay.RecordMonsterDefeatedAsync(new(characterId, kill.Code, (int)kill.Rank, dungeonId, (int)difficulty), ct).ConfigureAwait(false);
            if (finalBoss && kill.Rank == MonsterRank.Boss)
            {
                footprint.DemandEffects(new(accountId, characterId, session));
                await gameplay.RecordDungeonCompletedAsync(new(characterId, dungeonId, (int)difficulty), ct).ConfigureAwait(false);
            }
        }
        return grants with { Award = award, QuestProgress = report.NotificationSnapshot, QuestChanges = report.Changes };
    }

    private async ValueTask<MonsterKillEffects> GrantQuestItemsAsync(SelectedSessionIdentity? session, AccountId account, CharacterId character,
        IReadOnlyList<RolledDrop>? loot, PartyKey party, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(account, character, session));
        var empty = new MonsterKillEffects(null, null, null, []);
        if (party != default && (parties?.FindByKey(party) is not { } current
            || !current.Members.Any(x => x.CharacterId == character))) return empty;
        footprint.DemandEffects(new(account, character, session));
        var additions = await questItems.GrantAsync(account, character, loot, ct).ConfigureAwait(false);
        var (items, shared) = await QuestItemDropProjection.ReadAsync(
            additions, (clock ?? TimeProvider.System).GetUtcNow(), ct).ConfigureAwait(false);
        return empty with { QuestItems = items, AccountMaterials = shared };
    }
}
