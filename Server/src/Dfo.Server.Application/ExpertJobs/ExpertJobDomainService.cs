using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
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
    ICharacterTable characterOwners, ICharacterDomainService characters, ICharacterQuestLogDomainService quests,
    ICharacterSkillStateDomainService skills, ICharacterBusinessInventoryDomainService bags, IApplyEffectsDomainService effects,
    ISelectedSessionDomainService sessions, IExpertStoreDomainService stores, IDungeonParticipationDomainService dungeons,
    IDomainTransactionAccessor transactions, TimeProvider clock, DisjointerOperations disjointer,
    ExpertJobProductionOperations production)
    : IExpertJobDomainService
{
    public ICharacterExpertJob Get(CharacterId character) => new CharacterExpertJob(this, character);

    private static readonly IReadOnlyDictionary<int, byte> NoGrantedSkills =
        System.Collections.Immutable.ImmutableDictionary<int, byte>.Empty;

    private async ValueTask<bool> JoinFromQuestAsync(ExpertJobQuestGrant grant, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Expert job joins require a domain transaction.");
        var (owner, questId, type) = grant;
        if (await characterOwners.FindOwnerAsync(owner.Character, ct).ConfigureAwait(false) != owner.Account
            || await rules.FindAsync(type, ct).ConfigureAwait(false) is not { } definition
            || !definition.ConnectedQuests.Contains(questId))
            return false;

        var log = quests.LogOf(owner.Account, owner.Character);
        if (!(await log.ClearedAsync(ct).ConfigureAwait(false)).Any(entry => entry.QuestId == questId))
            return false;
        var current = await states.ReadAsync(owner.Character, ct).ConfigureAwait(false);
        if (current.Type != ExpertJobType.None) return false;

        await skills.ChangeGrantedSkillsAsync(owner.Character, NoGrantedSkills, definition.Skills, ct).ConfigureAwait(false);
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
        _ = transactions.Current ?? throw new InvalidOperationException("Expert job giveup requires a domain transaction.");
        var rejected = new ExpertJobGiveUpResult(ExpertJobGiveUpRejection.InvalidState);
        if (!sessions.IsInWorld(session) || dungeons.Find(session.CharacterId) is not null) return rejected;
        var current = await states.ReadAsync(session.CharacterId, ct).ConfigureAwait(false);
        if (await rules.FindAsync(current.Type, ct).ConfigureAwait(false) is not { } definition
            || current.GiveUpCount >= definition.GiveUpCosts.Count)
            return rejected;
        var owner = new OwnedCharacterId(session.AccountId, session.CharacterId);
        var context = new EffectExecutionContext(owner.Account, owner.Character, clock.GetUtcNow(), footprint, session);
        footprint.DemandEffects(context.Owner);
        var log = quests.LogOf(owner.Account, owner.Character);
        var cleared = await log.ClearedAsync(ct).ConfigureAwait(false);
        if (!cleared.Any(row => definition.ConnectedQuests.Contains(row.QuestId)))
            return rejected;

        var cost = definition.GiveUpCosts[current.GiveUpCount];
        var character = characters.Get(owner.Character);
        var balance = await character.GoldAsync(ct).ConfigureAwait(false);
        if (balance < cost) return new(ExpertJobGiveUpRejection.InsufficientGold);

        var costs = new List<DomainEffect>();
        if (cost > 0) costs.Add(new SpendGoldEffect(cost));
        if (definition.RemovedItem > 0)
        {
            var items = await bags.FindCarriedItemsAsync(owner.Character, definition.RemovedItem, ct).ConfigureAwait(false);
            if (items is null) return rejected;
            foreach (var item in items)
            {
                if (!item.IsPile || item.Count <= 0 || item.Snapshot.EquipmentLockId != 0 || item.BusinessSlot is not { } slot)
                    return rejected;
                costs.Add(new ConsumeFromContainerEffect(slot, item.ItemId, item.Count));
            }
        }

        var applied = await effects.ApplyAsync(context, costs, ct).ConfigureAwait(false);
        await skills.ChangeGrantedSkillsAsync(owner.Character, definition.Skills, NoGrantedSkills, ct).ConfigureAwait(false);
        await log.ForgetAsync(definition.ClearedOnGiveUp, ct).ConfigureAwait(false);

        var nextCount = checked((byte)Math.Min(current.GiveUpCount + 1, definition.GiveUpCosts.Count - 1));
        await states.WriteAsync(owner.Character, ExpertJobState.Empty with { GiveUpCount = nextCount }, ct).ConfigureAwait(false);
        var closure = stores.StageCloseOwned(session);
        return new(ExpertJobGiveUpRejection.None, applied.Changes<GoldEffectChange>().SingleOrDefault()?.Remaining ?? balance, nextCount, applied, closure);
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
        public ValueTask<bool> JoinFromQuestAsync(ExpertJobQuestGrant grant, CancellationToken ct) =>
            grant.Owner.Character == character ? service.JoinFromQuestAsync(grant, ct) : ValueTask.FromResult(false);
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
