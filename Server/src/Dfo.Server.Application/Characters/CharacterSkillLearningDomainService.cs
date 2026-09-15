using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillLearningDomainService(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionRuntimeDirectory runtime, ITownAreaDomainService townAreas,
    ICharacterTable characters, ICharacterEntryTable entries, ICharacterSkillTable skills,
    ICharacterSkillStateDomainService state, ICharacterSkillRuleService rules,
    IDomainTransactionAccessor transactions) : ICharacterSkillLearningDomainService
{
    public async ValueTask<CharacterSkillLearningResult> LearnAsync(SelectedSessionIdentity session, byte pageIndex,
        IReadOnlyList<CharacterSkillLearningChange> changes, byte mode, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
            throw new InvalidOperationException("Skill learning requires a domain transaction.");
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId
            || !runtime.TryGet(session, out var selected, out _) || selected is null)
            return new(CharacterSkillLearningRejection.NoSession);
        if (changes.Count is 0 or > byte.MaxValue || changes.Any(change =>
            change.SkillId == 0 || change.Refund != 0 || change.Increment == 0)
            || changes.Select(change => change.SkillId).Distinct().Count() != changes.Count)
            return new(CharacterSkillLearningRejection.InvalidRequest);
        if (mode != 0 || selected.World.Channel.PvpSkillProfileApplies
            || townAreas.Find(session.CharacterId) is not { } member
            || !session.Owns(member))
            return new(CharacterSkillLearningRejection.UnsupportedScenario);
        var entry = await entries.FindEntryAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.AccountId != session.AccountId.Value
            || await characters.FindOwnerAsync(session.CharacterId, cancellationToken).ConfigureAwait(false) != session.AccountId)
            return new(CharacterSkillLearningRejection.NoSession);
        // -1 is a locked second tree; otherwise the stored value names the active tree.
        if (pageIndex > 1 || (entry.SkillTreeIndex == -1 ? pageIndex != 0 : entry.SkillTreeIndex != pageIndex))
            return new(CharacterSkillLearningRejection.PageUnavailable);
        var facts = await state.ReadAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
        if (facts is null) return new(CharacterSkillLearningRejection.NoSession);
        var current = facts.Pages.SingleOrDefault(page => page.PageIndex == pageIndex)?.Skills ?? [];
        if (current.Select(skill => skill.SkillId).Distinct().Count() != current.Count)
            return new(CharacterSkillLearningRejection.IncompleteLedger);
        var target = current.ToDictionary(skill => checked((int)skill.SkillId));
        var occupied = current.Select(skill => (int)skill.Slot).ToHashSet();
        var changed = new List<CharacterSkillState>(changes.Count);
        foreach (var change in changes)
        {
            var rule = rules.Get(facts.Career.Job, change.SkillId);
            if (rule is null)
                return new(CharacterSkillLearningRejection.SkillUnavailable);
            var existing = target.GetValueOrDefault(change.SkillId);
            var level = (existing?.Level ?? 0) + change.Increment;
            if (level > byte.MaxValue)
                return new(CharacterSkillLearningRejection.SkillUnavailable);
            var slot = existing?.Slot ?? rule.FindAvailableSlot(occupied);
            if (slot is null) return new(CharacterSkillLearningRejection.NoSlot);
            if (slot is < 0 or >= byte.MaxValue || existing?.ExtraValues.Count > byte.MaxValue)
                return new(CharacterSkillLearningRejection.InvalidRequest);
            var learned = new CharacterSkillState(slot.Value, change.SkillId, (byte)level,
                existing?.ExtraValues ?? []);
            occupied.Add(slot.Value);
            target[change.SkillId] = learned;
            changed.Add(learned);
        }
        var finalLevels = target.ToDictionary(pair => pair.Key, pair => (int)pair.Value.Level);
        foreach (var learned in changed)
            if (!rules.Get(facts.Career.Job, (int)learned.SkillId)!
                .CanLearn(facts.Career, facts.Level, learned.Level, finalLevels))
                return new(CharacterSkillLearningRejection.SkillUnavailable);
        var pages = facts.Pages.Where(page => page.PageIndex != pageIndex)
            .Append(new CharacterSkillPageState(pageIndex, target.Values.OrderBy(skill => skill.Slot).ToArray())).ToArray();
        var ledger = rules.GetLedger(facts.Career, facts.Level, pages, facts.PointBasis);
        if (!ledger.IsComplete) return new(CharacterSkillLearningRejection.IncompleteLedger);
        if (pageIndex == 0 ? ledger.Page0Sp < 0 || ledger.Page0Tp < 0 : ledger.Page1Sp < 0 || ledger.Page1Tp < 0)
            return new(CharacterSkillLearningRejection.InsufficientPoints);

        // No rejection returns after the first write: storage failures must roll the whole batch back.
        foreach (var learned in changed)
        {
            if (current.Any(skill => skill.SkillId == learned.SkillId))
            {
                if (await skills.SetLevelAsync(session.CharacterId, pageIndex, learned.Slot,
                    learned.Level, cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The learned skill row changed during its transaction.");
            }
            else
                await skills.InsertAsync(session.CharacterId, pageIndex, learned.Slot, (int)learned.SkillId,
                    learned.Level, cancellationToken).ConfigureAwait(false);
        }
        return new(CharacterSkillLearningRejection.None, new(session, pageIndex, changed.AsReadOnly(), ledger));
    }
}
