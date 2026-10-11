using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record MonsterDeathSettlementRequest(SelectedSessionIdentity Reporter,
    IReadOnlyList<SelectedSessionIdentity> Participants, DungeonRunId RunId, long RoomVersion, ushort Actor);

public sealed record MonsterKillEffects(ExperienceAward? Award, QuestProgressSnapshot? QuestProgress,
    CommittedInventoryUpdateState? QuestItems, IReadOnlyList<InventoryRuntimeValueState> AccountMaterials)
{
    public IReadOnlyList<QuestProgressChange> QuestChanges { get; init; } = [];
}

public sealed record MonsterDeathParticipantResult(SelectedSessionIdentity Session,
    CharacterProgressionState Progress, CharacterSkillProtocolMirrorState SkillPoints,
    MonsterKillEffects Effects, AvailableQuestEntryState? AvailableQuests);

/// <summary>The runtime change becomes available only after the surrounding transaction commits.</summary>
public interface ICommittedActorDeath
{
    DungeonActorDeathChange? Change { get; }
}

public sealed record MonsterDeathSettlementResult(IReadOnlyList<MonsterDeathParticipantResult> Participants,
    ICommittedActorDeath? Death = null);

public interface IMonsterDeathDomainService
{
    ValueTask<MonsterDeathSettlementResult> SettleAsync(MonsterDeathSettlementRequest request, CancellationToken ct);
}

public interface IMonsterKillRewardsDomainService
{
    ValueTask<MonsterKillEffects> ApplyAsync(SelectedSessionIdentity? session,
        Dfo.Server.Data.Accounts.AccountId accountId, CharacterId characterId, IdentifiedKill kill,
        int dungeonId, DungeonDifficulty difficulty, double weight, int partyMemberCount,
        Dfo.Server.Data.Parties.PartyKey partyKey, bool finalBoss, DungeonRunId? runId, RoomActorId? actor,
        int? worldMapId, IReadOnlyList<RolledDrop>? loot, CancellationToken ct);
}
