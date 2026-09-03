using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// The character someone is playing, as something to ask.
/// </summary>
/// <remarks>
/// It holds an identity and the things it needs to look facts up, and nothing else. Every question
/// reads what is there when it is asked, so there is no copy of the character here to fall out of
/// step with a use case that was taken back.
///
/// What it supplies to a judgement is the identity work: which character, what it is, where it
/// stands, and whether it is waiting on a cooldown. What the judgement does with those is the
/// archive's business and lives in a service that could not find a character if it wanted to.
/// </remarks>
internal sealed class PlayingCharacter(
    CharacterId characterId,
    IItemUseJudgementService judgement,
    ICharacterItemCooldownDomainService cooldowns,
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    ISessionPresenceRegistry presence,
    ICharacterCarriedGoldTable carriedGold,
    ICarriedGoldRuleService goldRules) : ICharacter
{
    public CharacterId Id => characterId;

    public async ValueTask<int> GoldAsync(CancellationToken cancellationToken) =>
        (await carriedGold.FindAsync(characterId, cancellationToken).ConfigureAwait(false))
            ?.Carried ?? 0;

    /// <remarks>
    /// The cap is worked out from what the same read answers with -- the level and the stored
    /// upgrade -- and handed to the store, which applies it inside the statement that adds. That
    /// is why the cap is not compared here: a comparison made in this method would be a read
    /// followed by a write, and two arrivals answered at once would each pass it.
    /// <para>
    /// A character this server does not have is given nothing rather than opened a row. Inventing
    /// one here would put a figure on a character nobody can play.
    /// </para>
    /// </remarks>
    public async ValueTask<int> AddGoldAsync(int amount, CancellationToken cancellationToken)
    {
        if (await carriedGold.FindAsync(characterId, cancellationToken).ConfigureAwait(false)
            is not { } standing)
        {
            return 0;
        }

        var ceiling = goldRules.CarryLimitFor(standing.Level, standing.StoredCarryLimit);
        return await carriedGold
            .AddAsync(characterId, amount, ceiling, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// No cap is read: taking gold away cannot carry anyone past one. The refusal is the store's
    /// answer rather than a comparison made here, for the reason the addition's cap is the store's
    /// -- two charges answered at once must not both pass a test only one of them could afford.
    /// </remarks>
    public ValueTask<int?> TrySpendGoldAsync(int amount, CancellationToken cancellationToken) =>
        carriedGold.TryTakeAsync(characterId, amount, cancellationToken);

    public async ValueTask<ItemUsability> CanUseAsync(
        int itemId, CancellationToken cancellationToken)
    {
        var rule = await judgement.RuleForAsync(itemId, cancellationToken)
            .ConfigureAwait(false);

        // An unreadable declaration is refused rather than waved through. The archive names job
        // families and places this contract does not model -- a misspelt [prist], a [seria room] --
        // and reading "restriction I do not understand" as "no restriction" hands out exactly the
        // items the archive meant to withhold.
        if (rule is null || !rule.IsReadable)
        {
            return ItemUsability.NotAUsableItem;
        }

        // Nobody is playing this character, so there is nothing to compare against. Answering with
        // the archive's own refusal would tell the player their item is not a consumable.
        if (this.State() is not { } state)
        {
            return ItemUsability.CharacterStateUnavailable;
        }

        // The lookup is here because finding a cooldown needs a character and the judgement is not
        // allowed to know how to find one. What is reported is only what the register says: whether
        // a declaration with no cooldown at all can be waiting is a question about the declaration,
        // and it is answered where the declaration's other questions are.
        var coolingDown = cooldowns.IsCoolingDown(
            characterId, judgement.CooldownKeyFor(itemId, rule));

        return judgement.Judge(
            rule,
            new ItemUseCandidate(
                state.Progression.Level,
                CharacterJobFamilies.Resolve(state.Identity.Job),
                this.Place(),
                coolingDown));
    }

    private ActiveCharacterState? State()
    {
        var session = selectedSessions.FindByCharacter(characterId);
        if (session is null)
        {
            return null;
        }

        return runtimeSessions.TryGet(session, out var result, out _) ? result?.Character : null;
    }

    /// <summary>
    /// Where the character is, as far as the archive's vocabulary goes.
    /// </summary>
    /// <remarks>
    /// A dungeon is the only other place this server could put anyone, and dungeons are not served
    /// yet, so a character who is playing is in a town whether or not anyone recorded them there.
    /// <para>
    /// The default therefore reads as "town" rather than "unknown". That is the permissive
    /// direction, and it stops being safe the day dungeons are served: a character inside one whose
    /// presence record is missing would be allowed a village-only item. The place to fix it is the
    /// same change that starts serving dungeons, which is where the fact becomes real.
    /// </para>
    /// </remarks>
    private ItemUsePlace Place() =>
        presence.Find(characterId) is { StandingInTown: false }
            ? ItemUsePlace.Dungeon
            : ItemUsePlace.Village;
}

/// <summary>
/// The archive's declaration, judged against what a character is and where it stands.
/// </summary>
/// <remarks>
/// Nothing here can find a character, and that is the point. Every fact it compares arrives as a
/// value, so the four comparisons and the order they are made in can be checked without a session,
/// a directory or a database -- and the same judgement serves whoever else comes to need it. The
/// archive it judges against is read here too, so that a judgement and the effects planned from it
/// are made against one declaration rather than two lookups that could disagree.
/// </remarks>
internal sealed class ItemUseJudgementService(
    IItemUseRuleDefinitionSource rules) : IItemUseJudgementService
{
    public ValueTask<ItemUseRule?> RuleForAsync(int itemId, CancellationToken cancellationToken) =>
        rules.GetAsync(itemId, cancellationToken);

    public ItemCooldownKey CooldownKeyFor(int itemId, ItemUseRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.CooltimeGroup > 0
            ? ItemCooldownKey.ForGroup(rule.CooltimeGroup)
            : ItemCooldownKey.ForItem(itemId);
    }

    public ItemUsability Judge(ItemUseRule rule, ItemUseCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(candidate);

        // The permanent reasons come first. A cooldown expires, so reporting it in front of a level
        // the character has not reached sends the player away to wait for something that will not
        // help -- and cooldowns are shared, so the item that started the wait need not be the item
        // being refused. The order is the answer as much as the comparisons are.
        if (candidate.Level < rule.MinimumLevel)
        {
            return ItemUsability.LevelTooLow;
        }

        if (!rule.Jobs.Allows(candidate.JobFamily))
        {
            return ItemUsability.JobMismatch;
        }

        if (!rule.Places.Allows(candidate.Place))
        {
            return ItemUsability.PlaceNotAllowed;
        }

        // A declaration with no cooldown of its own cannot be waiting, whatever the register says
        // about the key it would have shared. Asked here rather than beside the lookup, so that the
        // two never answer differently about one declaration.
        return rule.CooldownMilliseconds > 0 && candidate.IsCoolingDown
            ? ItemUsability.OnCooldown
            : ItemUsability.Allowed;
    }
}
