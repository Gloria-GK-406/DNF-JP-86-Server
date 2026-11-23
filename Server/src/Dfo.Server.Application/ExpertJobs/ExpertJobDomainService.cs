using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.ExpertJobs;

internal sealed class ExpertJobRuleService(IExpertJobDefinitionSource source) : IExpertJobRuleService
{
    public ValueTask<ExpertJobDefinition?> FindAsync(ExpertJobType type, CancellationToken ct) => source.FindAsync(type, ct);
    public ValueTask<ExpertJobType?> FindDrawingJobAsync(int itemId, CancellationToken ct) => source.FindDrawingJobAsync(itemId, ct);

    public async ValueTask<bool> ContainsGrantedSkillAsync(IReadOnlySet<int> ids, CancellationToken ct)
    {
        foreach (var type in Enum.GetValues<ExpertJobType>())
            if (type != ExpertJobType.None && await FindAsync(type, ct).ConfigureAwait(false) is { } definition
                && definition.Skills.Keys.Any(ids.Contains))
                return true;
        return false;
    }
}

internal sealed class ExpertJobDomainService(IExpertJobStateStore states, IExpertJobRuleService rules,
    ICharacterTable characters, IQuestDefinitionDomainService definitions, IQuestDomainStore quests,
    ICharacterSkillStateDomainService skills, ICharacterSkillRuleService skillRules,
    ICharacterSkillTable skillRows, ICharacterSkillBuildTable skillBuilds, ICharacterCarriedGoldTable gold,
    ICharacterContainerDomainService containers, IApplyEffectsDomainService effects,
    ISelectedSessionDomainService sessions, IExpertStoreDomainService stores, IDungeonParticipationDomainService dungeons,
    IDomainTransactionAccessor transactions, TimeProvider clock, DisjointerOperations disjointer,
    ExpertJobProductionOperations production)
    : IExpertJobDomainService
{
    public ICharacterExpertJob Get(CharacterId character) => new CharacterExpertJob(this, character);

    private async ValueTask<bool> JoinFromQuestAsync(OwnedCharacterId owner, ushort questId, ExpertJobType type, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Expert job joins require a domain transaction.");
        if (await characters.FindOwnerAsync(owner.Character, ct).ConfigureAwait(false) != owner.Account
            || await rules.FindAsync(type, ct).ConfigureAwait(false) is not { } definition
            || !definition.ConnectedQuests.Contains(questId)
            || await definitions.FindAsync(questId, ct).ConfigureAwait(false) is not { } quest
            || !quest.Rewards.Any(reward => reward.Kind == QuestRewardKind.ExpertJob && reward.Values.SequenceEqual([(int)type])))
            return false;

        var questState = quests.SessionIn(transaction);
        if (!(await questState.Cleared.ReadAllAsync(owner, ct).ConfigureAwait(false))
                .Any(entry => entry.QuestId == questId && entry.Mark != 0))
            return false;
        var current = await states.ReadAsync(owner.Character, ct).ConfigureAwait(false);
        if (current.Type != ExpertJobType.None) return false;

        await ApplySkillsAsync(owner.Character, definition, learn: true, ct).ConfigureAwait(false);
        await states.WriteAsync(owner.Character, new(type, 0, current.GiveUpCount,
            type == ExpertJobType.Disjointer ? 1 : 0,
            type == ExpertJobType.Disjointer ? definition.InitialEndurance : 0,
            type == ExpertJobType.Enchanter ? definition.InitialEndurance : 0,
            definition.RecipesAt(0)), ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<ExpertJobGiveUpResult> GiveUpAsync(SelectedSessionIdentity session, IEffectFootprintRecorder footprint,
        CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Expert job giveup requires a domain transaction.");
        var rejected = new ExpertJobGiveUpResult(ExpertJobGiveUpRejection.InvalidState);
        if (!sessions.IsInWorld(session) || dungeons.Find(session.CharacterId) is not null) return rejected;
        var current = await states.ReadAsync(session.CharacterId, ct).ConfigureAwait(false);
        if (await rules.FindAsync(current.Type, ct).ConfigureAwait(false) is not { } definition
            || current.GiveUpCount >= definition.GiveUpCosts.Count)
            return rejected;
        var owner = new OwnedCharacterId(session.AccountId, session.CharacterId);
        var context = new EffectExecutionContext(owner.Account, owner.Character, clock.GetUtcNow(), footprint, session);
        footprint.DemandEffects(context.Owner);
        var questState = quests.SessionIn(transaction);
        var cleared = await questState.Cleared.ReadAllAsync(owner, ct).ConfigureAwait(false);
        if (!cleared.Any(row => row.Mark != 0 && definition.ConnectedQuests.Contains(row.QuestId)))
            return rejected;

        var cost = definition.GiveUpCosts[current.GiveUpCount];
        var purse = await gold.FindAsync(owner.Character, ct).ConfigureAwait(false);
        var bag = containers.FindContainer(owner.Character, InventoryContainerType.Main);
        if (purse is null || bag is null) return rejected;
        if (purse.Carried < cost) return new(ExpertJobGiveUpRejection.InsufficientGold);

        var costs = new List<DomainEffect>();
        if (cost > 0) costs.Add(new SpendGoldEffect(cost));
        if (definition.RemovedItem > 0)
        {
            foreach (var item in (await bag.GetAllAsync(ct).ConfigureAwait(false)).Where(item => item.ItemId == definition.RemovedItem))
            {
                if (!item.IsPile || item.Count <= 0 || item.Snapshot.EquipmentLockId != 0)
                    return rejected;
                var slot = item.BusinessSlot ?? A21InventoryLayout.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, item.StorageSlotIndex));
                costs.Add(new ConsumeFromContainerEffect(slot, item.ItemId, item.Count));
            }
        }

        var applied = await effects.ApplyAsync(context, costs, ct).ConfigureAwait(false);
        await ApplySkillsAsync(owner.Character, definition, learn: false, ct).ConfigureAwait(false);
        foreach (var row in await questState.Carried.ReadAllAsync(owner, ct).ConfigureAwait(false))
            if (definition.ClearedOnGiveUp.Contains(row.QuestId)) questState.Carried.Clear(owner, row.Slot);
        foreach (var row in cleared)
            if (definition.ClearedOnGiveUp.Contains(row.QuestId)) questState.Cleared.Clear(owner, row.QuestId);
        foreach (var row in await questState.Tracked.ReadAllAsync(owner, ct).ConfigureAwait(false))
            if (definition.ClearedOnGiveUp.Contains(row.QuestId)) questState.Tracked.Clear(owner, row.SlotIndex);

        var nextCount = checked((byte)Math.Min(current.GiveUpCount + 1, definition.GiveUpCosts.Count - 1));
        await states.WriteAsync(owner.Character, ExpertJobState.Empty with { GiveUpCount = nextCount }, ct).ConfigureAwait(false);
        var closure = stores.StageCloseOwned(session);
        return new(ExpertJobGiveUpRejection.None, purse.Carried - cost, nextCount, applied, closure);
    }

    private async ValueTask ApplySkillsAsync(CharacterId character, ExpertJobDefinition definition, bool learn, CancellationToken ct)
    {
        if (definition.Skills.Count == 0) return;
        if (learn) await skillBuilds.EnsureAsync(character, ct).ConfigureAwait(false);
        var facts = await skills.ReadAsync(character, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("The expert job character has no skill profile.");
        foreach (var page in facts.Pages)
        {
            var occupied = page.Skills.Select(skill => (int)skill.Slot).ToHashSet();
            foreach (var (id, level) in definition.Skills)
            {
                var existing = page.Skills.SingleOrDefault(skill => skill.SkillId == id);
                if (!learn)
                {
                    if (existing is not null && await skillRows.RemoveAsync(character, page.PageIndex, existing.Slot, ct).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException("Expert job skill changed before removal.");
                    continue;
                }
                if (existing is not null)
                {
                    if (existing.Level < level && await skillRows.SetLevelAsync(character, page.PageIndex, existing.Slot, level, ct).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException("Expert job skill changed before grant.");
                    continue;
                }
                var slot = skillRules.Get(facts.Career.Job, id)?.FindAvailableSlot(occupied)
                    ?? Enumerable.Range(0, 204).FirstOrDefault(candidate => !occupied.Contains(candidate), -1);
                if (slot < 0) throw new InvalidDataException($"No skill slot for expert-job skill {id}.");
                await skillRows.InsertAsync(character, page.PageIndex, slot, id, level, ct).ConfigureAwait(false);
                occupied.Add(slot);
            }
        }
    }

    private sealed class CharacterExpertJob(ExpertJobDomainService service, CharacterId character) : ICharacterExpertJob
    {
        public ValueTask<ExpertJobState> GetStateAsync(CancellationToken ct) => service.ReadAsync(character, ct);
        public ValueTask<IDisjointerDomainService?> GetDisjointerAsync(CancellationToken ct) => service.GetDisjointerAsync(character, ct);
        public ValueTask<IAlchemistDomainService?> GetAlchemistAsync(CancellationToken ct) => service.GetAlchemistAsync(character, ct);
        public ValueTask<IDollControllerDomainService?> GetDollControllerAsync(CancellationToken ct) => service.GetDollControllerAsync(character, ct);
        public ValueTask<IEnchanterDomainService?> GetEnchanterAsync(CancellationToken ct) => service.GetEnchanterAsync(character, ct);
        public ValueTask<ExpertJobStudyResult?> TryLearnRecipeAsync(SelectedSessionIdentity session, BusinessSlot source,
            ItemReference drawing, IEffectFootprintRecorder footprint, CancellationToken ct) =>
            service.TryLearnRecipeAsync(character, session, source, drawing, footprint, ct);
        public ValueTask<bool> JoinFromQuestAsync(OwnedCharacterId owner, ushort questId, ExpertJobType type, CancellationToken ct) =>
            owner.Character == character ? service.JoinFromQuestAsync(owner, questId, type, ct) : ValueTask.FromResult(false);
        public ValueTask<ExpertJobGiveUpResult> GiveUpAsync(SelectedSessionIdentity session, IEffectFootprintRecorder footprint, CancellationToken ct) =>
            session.CharacterId == character ? service.GiveUpAsync(session, footprint, ct)
                : ValueTask.FromResult(new ExpertJobGiveUpResult(ExpertJobGiveUpRejection.InvalidState));
    }

    private ValueTask<ExpertJobState> ReadAsync(CharacterId character, CancellationToken ct) => states.ReadAsync(character, ct);

    private async ValueTask<IDisjointerDomainService?> GetDisjointerAsync(CharacterId character, CancellationToken ct) =>
        (await ReadAsync(character, ct).ConfigureAwait(false)).Type == ExpertJobType.Disjointer
            ? disjointer.Bind(character) : null;

    private async ValueTask<IAlchemistDomainService?> GetAlchemistAsync(CharacterId character, CancellationToken ct) =>
        (await ReadAsync(character, ct).ConfigureAwait(false)).Type == ExpertJobType.Alchemist
            ? production.BindAlchemist(character) : null;

    private async ValueTask<IDollControllerDomainService?> GetDollControllerAsync(CharacterId character, CancellationToken ct) =>
        (await ReadAsync(character, ct).ConfigureAwait(false)).Type == ExpertJobType.DollController
            ? production.BindDollController(character) : null;

    private async ValueTask<IEnchanterDomainService?> GetEnchanterAsync(CharacterId character, CancellationToken ct) =>
        (await ReadAsync(character, ct).ConfigureAwait(false)).Type == ExpertJobType.Enchanter
            ? production.BindEnchanter(character) : null;

    private ValueTask<ExpertJobStudyResult?> TryLearnRecipeAsync(CharacterId character, SelectedSessionIdentity session,
        BusinessSlot source, ItemReference drawing, IEffectFootprintRecorder footprint, CancellationToken ct) =>
        production.TryLearnRecipeAsync(character, session, source, drawing, footprint, ct);
}
