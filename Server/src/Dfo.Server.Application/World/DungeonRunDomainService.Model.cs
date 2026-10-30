using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.World;

internal sealed partial class DungeonRunDomainService
{
    private sealed class DungeonRunAggregate
    {
        private readonly IPartyService parties;
        private readonly ImmutableArray<DungeonRunParticipant> entryParticipants;
        // The entry roster: accounts of who entered, and the bound that keeps anyone else out of the
        // membership. It is not the membership; the party answers that.
        private readonly ImmutableDictionary<CharacterId, AccountId> entryAccounts;
        // Fixed to the entrants at entry; only each entrant's progress changes.
        private readonly ImmutableDictionary<CharacterId, ParticipantProgress> progressByCharacter;
        private readonly List<DungeonKillRecord> kills = [];
        private int nextGroundItemNumber = 1;

        public DungeonRunAggregate(
            DungeonRunId runId,
            PartyKey partyKey,
            IPartyService parties,
            ImmutableArray<DungeonRunParticipant> participants,
            DungeonLayout layout,
            DungeonDifficulty difficulty,
            DateTimeOffset startedAt,
            DungeonCellSeed initialCell)
        {
            this.RunId = runId;
            this.PartyKey = partyKey;
            this.parties = parties;
            this.entryParticipants = participants;
            this.Layout = layout;
            this.ModeState = layout.Mode is DungeonRunMode.LostLand ? new DungeonRunModeState.LostLand(new()) : null;
            this.Difficulty = difficulty;
            this.StartedAt = startedAt;
            this.CurrentCell = layout.StartCell;
            this.Cells = new Dictionary<MazeCell, DungeonCellAggregate>
            {
                [initialCell.Cell] = this.CreateCell(initialCell),
            };
            this.VisitedCells = ImmutableHashSet.Create(layout.StartCell);
            this.entryAccounts = participants.ToImmutableDictionary(
                participant => participant.CharacterId, participant => participant.AccountId);
            this.progressByCharacter = participants.ToImmutableDictionary(
                participant => participant.CharacterId,
                _ => new ParticipantProgress());
        }

        // A21 resolves ground objects across retained rooms by number alone.
        // Keep the wire identity unique for this run, even after pickup or a room revisit.
        public DungeonCellAggregate CreateCell(DungeonCellSeed seed) =>
            new(seed, this.AllocateGroundItemNumbers);

        private int AllocateGroundItemNumbers(int count)
        {
            if (count > ushort.MaxValue - this.nextGroundItemNumber + 1)
                throw new InvalidOperationException(
                    "The dungeon run exhausted its stable ground-item number space.");
            var first = this.nextGroundItemNumber;
            this.nextGroundItemNumber += count;
            return first;
        }

        public DungeonRunId RunId { get; }
        public PartyKey PartyKey { get; }
        /// <summary>The run's members: its party's current members who entered it, with the party's slots and leader.</summary>
        public ImmutableArray<DungeonRunParticipant> Participants
        {
            get
            {
                var membership = this.parties.FindByKey(this.PartyKey);
                if (membership is null)
                    return [];
                return [.. membership.Members
                    .Where(member => this.entryAccounts.ContainsKey(member.CharacterId))
                    .Select(member => new DungeonRunParticipant(
                        this.entryAccounts[member.CharacterId],
                        member.CharacterId,
                        member.Slot,
                        member.CharacterId == membership.LeaderCharacterId))];
            }
        }
        /// <summary>Everyone who entered, in entry order.</summary>
        public IEnumerable<CharacterId> EntrantIds =>
            this.entryParticipants.Select(participant => participant.CharacterId);
        public DungeonRunParticipant Leader =>
            this.Participants.Single(participant => participant.IsLeader);
        public DateTimeOffset? ClearedAt { get; set; }
        public DungeonLayout Layout { get; }
        public DungeonRunModeState? ModeState { get; set; }
        // Lost Land views of the slot; only a Lost Land run has one, so writes assume it.
        public LostLandFeverState? LostLandFever
        {
            get => (this.ModeState as DungeonRunModeState.LostLand)?.Fever;
            set => this.ModeState = (DungeonRunModeState.LostLand)this.ModeState! with { Fever = value! };
        }
        public LostLandExplorationState? LostLandExploration
        {
            get => (this.ModeState as DungeonRunModeState.LostLand)?.Exploration;
            set => this.ModeState = (DungeonRunModeState.LostLand)this.ModeState! with { Exploration = value };
        }
        public DungeonDifficulty Difficulty { get; }
        public DateTimeOffset StartedAt { get; }
        public MazeCell CurrentCell { get; set; }
        public int CurrentRoomMapId => this.Cells[this.CurrentCell].MapId;
        public Dictionary<MazeCell, DungeonCellAggregate> Cells { get; }
        public long RoomVersion { get; set; }
        public long EntryRevision { get; set; } = 1;
        public long RuntimeRevision { get; set; }
        public ImmutableHashSet<MazeCell> VisitedCells { get; set; }
        public Dictionary<CharacterId, DungeonKillExperienceSummary> KillExperiences { get; } = [];

        public HashSet<ParticipantEffectKey> CompletedKillEffects { get; } = [];
        // Derived from committed deaths: no second mutable element-state container to drift.
        public IEnumerable<int> KilledMonsterCodes => this.kills.Select(x => x.MonsterCode);
        public HashSet<CharacterId> BuffParticipants { get; } = [];
        public HashSet<CharacterId> ReadyParticipants { get; } = [];

        public HashSet<CharacterId> DeadParticipants { get; } = [];
        public Dictionary<CharacterId, int> RevivalCounts { get; } = [];
        public Dictionary<CharacterId, int> RevivalCoinPayments { get; } = [];
        public List<DungeonCardSelection> CardSelections { get; } = [];
        public HashSet<ClearRewardCardKind> RevealedCardKinds { get; } = [];

        public void RecordKill(DungeonKillRecord kill)
        {
            if (this.kills.Any(existing => existing.Actor == kill.Actor))
                throw new InvalidOperationException("One dungeon actor cannot die twice.");
            this.kills.Add(kill);
        }

        public IReadOnlyList<DungeonCardSelection> PublishedCardSelections() =>
            this.PublishedCardSelections(this.Participants);

        private IReadOnlyList<DungeonCardSelection> PublishedCardSelections(
            ImmutableArray<DungeonRunParticipant> members) =>
            [.. this.CardSelections
                .Where(selection => this.RevealedCardKinds.Contains(selection.Kind)
                    && members.Any(member => member.CharacterId == selection.CharacterId))
                .OrderBy(selection => selection.Kind)
                .ThenBy(selection => selection.Index)];

        public bool IsMember(CharacterId characterId) =>
            this.Participants.Any(participant => participant.CharacterId == characterId);

        /// <summary>Whether the character entered this run and has not departed it yet.</summary>
        public bool HoldsRunState(CharacterId characterId) =>
            this.progressByCharacter.TryGetValue(characterId, out var progress) && !progress.Released;

        /// <summary>Everyone still holding state in this run: current members first, by slot.</summary>
        public CharacterId[] Unreleased() =>
            [.. this.Participants.OrderBy(participant => participant.Slot)
                .Select(participant => participant.CharacterId)
                .Concat(this.entryParticipants.OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId))
                .Distinct()
                .Where(this.HoldsRunState)];

        public ParticipantProgress ProgressFor(CharacterId characterId) =>
            this.progressByCharacter[characterId];

        public DungeonEntryChange EntryChange(
            DungeonEntryChangeKind kind,
            CharacterId viewer) =>
            new(
                kind,
                this.ViewFor(viewer),
                this.EntryRevision,
                [.. this.Participants
                    .OrderBy(participant => participant.Slot)
                    .Select(participant => participant.CharacterId)]);

        public DungeonRunState ViewFor(CharacterId characterId)
        {
            var progress = this.ProgressFor(characterId);
            var members = this.Participants;
            return new DungeonRunState(
                this.Layout,
                this.CurrentCell,
                this.CurrentRoomMapId,
                this.VisitedCells,
                this.StartedAt,
                this.Difficulty,
                progress.IsSettled,
                progress.ClearRewardCards,
                this.RunId,
                this.RoomVersion,
                members.Single(participant => participant.CharacterId == characterId),
                members,
                this.EntryRevision,
                this.ReadyParticipants.ToImmutableHashSet(),
                this.Cells[this.CurrentCell].ClearOpen,
                this.RuntimeRevision,
                this.PublishedCardSelections(members),
                progress.CardLayoutOpen,
                this.Cells[this.CurrentCell].Snapshot())
            {
                PartyKey = this.PartyKey,
                DeadParticipants = this.DeadParticipants.ToImmutableHashSet(),
                RevivalCounts = this.RevivalCounts.ToImmutableDictionary(),
                RevivalCoinPayments = this.RevivalCoinPayments.ToImmutableDictionary(),
                EntryPurposeOwner = this.entryParticipants.Single(participant => participant.IsLeader),
                Kills = this.kills.ToImmutableArray(),
                ClearedAt = this.ClearedAt,
                ClearedCells = this.Cells.Where(x => x.Value.IsCleared).Select(x => x.Key).ToImmutableHashSet(),
                KillExperience = this.KillExperiences.GetValueOrDefault(characterId) ?? new(),
                ModeState = this.ModeState,
            };
        }
    }

    private sealed class DungeonCellAggregate
    {
        private readonly DungeonCellObjectState[] objects;
        public IReadOnlyList<DungeonRoomMechanismState> Mechanisms { get; set; } = [];
        public bool MechanismBlocksClear { get; set; }
        public readonly Dictionary<int, long> ObjectReservations = [];
        public readonly Dictionary<int, Queue<int>> ObjectQueues;
        private readonly Dictionary<ushort, DungeonCellActorState> actors;
        private readonly Dictionary<int, DungeonCellHellGroupState> hellGroups;
        private readonly Dictionary<ushort, DungeonGroundItem> groundItems;
        private readonly Func<int, int> allocateGroundItemNumbers;

        public DungeonCellAggregate(DungeonCellSeed seed, Func<int, int> allocateGroundItemNumbers)
        {
            this.MechanismBlocksClear = seed.MechanismBlocksClear;
            this.Mechanisms = seed.Mechanisms.Select(definition => new DungeonRoomMechanismState(definition)).ToArray();
            this.objects = seed.Objects.Select(obj => obj with { PendingLoot = obj.PendingLoot.ToArray() }).ToArray();
            this.ObjectQueues = this.objects.Select((value, index) => (value, index))
                .Where(entry => !entry.value.IsConsumed)
                .GroupBy(entry => entry.value.Object.Code)
                .ToDictionary(group => group.Key, group => new Queue<int>(group.Select(entry => entry.index)));
            this.actors = seed.Actors.ToDictionary(
                actor => actor.Sequence,
                actor => actor with { PendingLoot = actor.PendingLoot.ToArray() });
            this.hellGroups = seed.HellGroups.ToDictionary(group => group.GroupIndex,
                group => group with { ActorSequences = group.ActorSequences.ToArray(), PendingLoot = group.PendingLoot.ToArray() });
            foreach (var group in this.hellGroups.Values)
                if (group.ActorSequences.Count == 0 || group.ActorSequences.Distinct().Count() != group.ActorSequences.Count
                    || group.ActorSequences.Any(sequence => !this.actors.TryGetValue(sequence, out var actor)
                        || !actor.IsHellTemplate || actor.HellGroupIndex != group.GroupIndex))
                    throw new InvalidDataException("Hell reward group membership does not match cell actors.");
            if (this.actors.Values.Any(actor => actor.IsHellTemplate && (actor.HellGroupIndex is not { } id
                || !this.hellGroups.TryGetValue(id, out var group) || !group.ActorSequences.Contains(actor.Sequence))))
                throw new InvalidDataException("Hell cell actor has no reward group.");
            this.allocateGroundItemNumbers = allocateGroundItemNumbers;
            var first = allocateGroundItemNumbers(seed.GroundItems.Count);
            this.groundItems = seed.GroundItems
                .Select((item, index) => item with { Number = checked((ushort)(first + index)) })
                .ToDictionary(item => item.Number);
            this.Cell = seed.Cell;
            this.MapId = seed.MapId;
            this.FirstActorSequence = seed.FirstActorSequence;
        }

        public MazeCell Cell { get; }
        public int MapId { get; }
        public ushort FirstActorSequence { get; }
        public long Revision { get; set; }
        public bool ClearOpen { get; set; }
        public bool IsCleared => !this.MechanismBlocksClear && this.actors.Values.Where(x => x.BlocksClear).All(x => x.IsDead);
        public bool WillClearAfterDeath(ushort sequence, IReadOnlyList<ushort> suppressed) =>
            !this.MechanismBlocksClear && this.actors.Values.All(actor => !actor.BlocksClear || actor.IsDead
                || actor.Sequence == sequence || suppressed.Contains(actor.Sequence));
        public bool HasLivingBossExcept(ushort sequence) => this.actors.Values.Any(actor =>
            actor.Sequence != sequence && actor.BlocksClear && actor.Actor.IsBoss && !actor.IsDead);

        public DungeonCellObjectState ObjectAt(int index) => this.objects[index];

        public bool HasUnopenedRewardObject(int dungeonId, int objectIndex, ISealedTreasureRuleService rules) =>
            this.objects.Any(x => x.Object.Id.Section == DungeonObjectSection.SpecialPassive
                && x.Object.Id.Index == objectIndex && !x.IsConsumed
                && rules.RequiresDestroyedObject(dungeonId, x.Object.Code));

        public void ConsumeObject(int index)
        {
            this.objects[index] = this.objects[index] with { IsConsumed = true, PendingLoot = [] };
            this.Revision++;
        }

        public void AddActors(IReadOnlyList<DungeonCellActorState> additions)
        {
            foreach (var actor in additions) this.actors.Add(actor.Sequence, actor);
            this.Revision++;
        }

        public DungeonCellActorState? FindActor(ushort sequence) =>
            this.actors.GetValueOrDefault(sequence);

        public DungeonGroundItem? FindGroundItem(ushort number) =>
            this.groundItems.GetValueOrDefault(number);

        public DungeonGroundItem? RemoveGroundItem(ushort number)
        {
            if (!this.groundItems.Remove(number, out var item))
                return null;
            this.Revision++;
            return item;
        }

        public bool CanClaimDeath(ushort sequence) =>
            this.actors.TryGetValue(sequence, out var actor) && !actor.IsDead
                && actor.Actor.Declaration?.IsDummy != true;

        public IReadOnlyList<DungeonGroundItem>? CommitActorDeath(
            ushort sequence,
            Func<RolledDrop, bool> mayMaterialize, IReadOnlyList<RolledDrop>? resolvedLoot, IReadOnlyList<RolledDrop> bonus)
        {
            if (!this.actors.TryGetValue(sequence, out var actor) || actor.IsDead)
                return null;

            var completedGroup = actor.HellGroupIndex is { } groupId
                && this.hellGroups.TryGetValue(groupId, out var group) && !group.IsSettled
                && group.ActorSequences.All(member => member == sequence || this.actors[member].IsDead)
                    ? group : null;
            var rewards = (resolvedLoot ?? actor.PendingLoot).Concat(completedGroup is not null && actor.EligibleForHellGroupReward
                ? completedGroup.PendingLoot : []).Concat(bonus);
            var added = this.AppendGroundItems(rewards
                .Where(rolled => rolled.Delivery == RolledDropDelivery.Ground)
                .Where(mayMaterialize)
                .Select(rolled =>
                    new DungeonGroundItemDraft(
                        rolled.ItemId,
                        rolled.Count,
                        rolled.Durability,
                        rolled.Kind == DropItemKind.Equipment,
                        DungeonGroundItemSource.FromActor(sequence),
                        rolled.Rarity,
                        rolled.Kind,
                        rolled.OwnerCharacterId)).ToArray());

            this.actors[sequence] = actor with { IsDead = true, PendingLoot = [], LostLandBonus = null };
            if (completedGroup is not null)
                this.hellGroups[completedGroup.GroupIndex] = completedGroup with { IsSettled = true, PendingLoot = [] };
            this.Revision++;
            return added;
        }

        // Part of the final boss death change: no separate ground roll, reward, or revision.
        public IReadOnlyList<ushort> SuppressibleEnemies(Func<ushort, bool> canSuppress) => this.actors.Values
                .Where(actor => actor.BlocksClear && !actor.Actor.IsBoss
                    && actor.Actor.Declaration?.Source != DungeonActorSpawnSource.SpecialPassiveObject
                    && !actor.IsDead && canSuppress(actor.Sequence))
                .OrderBy(actor => actor.Sequence)
                .Select(actor => actor.Sequence)
                .ToArray();

        public void SuppressEnemies(IReadOnlyList<ushort> sequences)
        {
            foreach (var sequence in sequences)
                this.actors[sequence] = this.actors[sequence] with { IsDead = true, PendingLoot = [], LostLandBonus = null };
        }

        public IReadOnlyList<DungeonGroundItem> AppendGroundItems(
            IReadOnlyList<DungeonGroundItemDraft> items)
        {
            if (items.Any(draft => draft.Count <= 0))
                throw new ArgumentOutOfRangeException(nameof(items), "Ground item count must be positive.");
            var first = this.allocateGroundItemNumbers(items.Count);

            var added = new List<DungeonGroundItem>(items.Count);
            for (var index = 0; index < items.Count; index++)
            {
                var draft = items[index];
                added.Add(new DungeonGroundItem(
                    checked((ushort)(first + index)),
                    draft.ItemId,
                    draft.Count,
                    draft.Durability,
                    draft.IsEquipment,
                    draft.Source,
                    draft.Rarity,
                    draft.Kind,
                    draft.OwnerCharacterId));
            }

            foreach (var item in added)
                this.groundItems.Add(item.Number, item);
            return added;
        }

        public DungeonCellState Snapshot() => new(
            this.Cell,
            this.MapId,
            this.Revision,
            this.FirstActorSequence,
            [.. this.actors.Values.OrderBy(actor => actor.Sequence)
                .Select(actor => actor with { PendingLoot = actor.PendingLoot.ToArray() })],
            [.. this.groundItems.Values.OrderBy(item => item.Number)],
            this.ClearOpen)
        {
            MechanismBlocksClear = this.MechanismBlocksClear,
            Mechanisms = this.Mechanisms.ToArray(),
            Objects = this.objects.Select(obj => obj with { PendingLoot = obj.PendingLoot.ToArray() }).ToArray(),
            HellGroups = this.hellGroups.Values.OrderBy(group => group.GroupIndex)
                .Select(group => group with { ActorSequences = group.ActorSequences.ToArray(), PendingLoot = group.PendingLoot.ToArray() }).ToArray(),
        };
    }

    private sealed class ParticipantProgress
    {
        public bool IsSettled { get; set; }
        public ClearRewardCardSet? ClearRewardCards { get; set; }
        public bool CardLayoutOpen { get; set; }
        /// <summary>Set once the character has departed; the progress itself is kept.</summary>
        public bool Released { get; set; }
    }
}
