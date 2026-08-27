using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Quest logs held in memory, for tests about what a service decides rather than about what a
/// database does with it.
/// </summary>
/// <remarks>
/// Changes land as soon as they are made rather than when a transaction settles, and every write
/// is kept whole, in the order it arrived. That keeps these tests about the service under test;
/// the settling itself is proven against the real store.
///
/// Only what a test has needed is implemented. The rest refuses rather than answering, because
/// the verbs left out are the ones with rules of their own -- where a quest goes, what finishing
/// it records -- and a double that reimplemented them would be a second, unchecked copy of the
/// judgement the real log exists to hold. Whoever needs one decides then what it should say.
/// </remarks>
internal sealed class RecordingQuestLogs : ICharacterQuestLogDomainService
{
    /// <summary>What each character is carrying, seeded by the test and changed by the log.</summary>
    public Dictionary<int, List<CarriedQuest>> Carried { get; } = [];

    /// <summary>Every progress change asked for, whole, in the order it was asked for.</summary>
    public List<(CarriedQuest AsLastSeen, int TriggerValue)> ProgressWrites { get; } = [];

    public IQuestLog LogOf(AccountId accountId, CharacterId characterId) =>
        new Log(this, characterId);

    private List<CarriedQuest> Held(CharacterId characterId)
    {
        if (!this.Carried.TryGetValue(characterId.Value, out var held))
        {
            held = [];
            this.Carried.Add(characterId.Value, held);
        }

        return held;
    }

    private sealed class Log(RecordingQuestLogs logs, CharacterId characterId) : IQuestLog
    {
        public CharacterId CharacterId => characterId;

        public ValueTask<IReadOnlyList<CarriedQuest>> CarriedAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CarriedQuest>>(
                [.. logs.Held(characterId).OrderBy(quest => quest.Slot)]);

        public ValueTask<CarriedQuest?> FindCarriedAsync(
            int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                logs.Held(characterId).FirstOrDefault(quest => quest.QuestId == questId));

        public ValueTask<IReadOnlyList<ClearedQuest>> ClearedAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ClearedQuest>>([]);

        public ValueTask<IReadOnlyList<int>> TrackedAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<int>>([]);

        public ValueTask<QuestAcceptance> AcceptAsync(
            QuestDefinition definition, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "Taking a quest on decides a slot; no test has needed that decided in memory.");

        public ValueTask<bool> RecordProgressAsync(
            CarriedQuest asLastSeen, int triggerValue, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(asLastSeen);
            var held = logs.Held(characterId);
            var index = held.FindIndex(quest =>
                quest.Slot == asLastSeen.Slot && quest.QuestId == asLastSeen.QuestId);
            if (index < 0)
            {
                return ValueTask.FromResult(false);
            }

            logs.ProgressWrites.Add((asLastSeen, triggerValue));
            held[index] = held[index] with { TriggerValue = triggerValue };
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> CompleteAsync(
            int questId, byte mark, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "Finishing a quest writes two lists at once; no test has needed that in memory.");

        public ValueTask<bool> GiveUpAsync(int questId, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "No test has needed a quest given up in memory; the real log proves that path.");

        public ValueTask TrackAsync(
            IReadOnlyList<int> questIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "No test has needed a tracking selection in memory.");
    }
}
