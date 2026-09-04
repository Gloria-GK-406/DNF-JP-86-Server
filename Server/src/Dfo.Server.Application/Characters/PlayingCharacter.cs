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
    ISessionPresenceDomainService presence,
    ICharacterCarriedGoldTable carriedGold,
    ICarriedGoldRuleService goldRules,
    ICharacterTable characters,
    ICharacterSkillPointDomainService skillPoints) : ICharacter
{
    public CharacterId Id => characterId;

    /// <remarks>
    /// The row, for the reason the progress below is read from the row: a session carries a copy of
    /// what the character is, and a copy is what somebody has to remember to correct. Nothing
    /// corrects this one -- a character's job changes when it awakens -- and until now the only way
    /// to ask what a character was, holding nothing but its identifier, was to find its session
    /// first and read that copy.
    /// <para>
    /// Two of these four are also on the standing read, which prices what a character is entitled
    /// to. They are not read through it: that read needs the level beside the job and this question
    /// must not depend on how far along the character is. The overlap is two columns of one row,
    /// not two sources.
    /// </para>
    /// </remarks>
    public async ValueTask<CharacterIdentityState?> IdentityAsync(
        CancellationToken cancellationToken) =>
        await characters.FindIdentityAsync(characterId, cancellationToken).ConfigureAwait(false)
            is { } identity
            ? new CharacterIdentityState(
                characterId,
                checked((ushort)identity.SlotIndex),
                identity.Name,
                checked((byte)identity.Job),
                checked((byte)identity.GrowType))
            : null;

    /// <remarks>
    /// The row, not the session. A session carries a copy of the character's standing and every
    /// path that moves it has to remember to correct that copy; the row is what those paths write
    /// to, so it is the one answer that cannot be a level behind.
    /// <para>
    /// Read straight off the table rather than through the service that awards experience. That
    /// service publishes no read: what it offers is the award itself, and adding a plain read to it
    /// would put a second door on this row without putting a rule behind it. Reaching the table is
    /// what this class already does for gold, and for the same reason.
    /// </para>
    /// </remarks>
    public async ValueTask<CharacterProgressionState?> ProgressAsync(
        CancellationToken cancellationToken) =>
        await characters.FindProgressAsync(characterId, cancellationToken).ConfigureAwait(false)
            is { } progress
            ? new CharacterProgressionState(
                checked((byte)Math.Clamp(progress.Level, 1, byte.MaxValue)),
                checked((uint)Math.Max(0, progress.Experience)))
            : null;

    /// <remarks>
    /// Handed straight on. Working the ledger out needs the archive as well as four tables, and
    /// what it comes to is the same answer whoever asks -- so it lives in one service that anything
    /// can reach, and a character is one of the things that reaches it rather than a second place
    /// that knows how.
    /// </remarks>
    public ValueTask<CharacterSkillProtocolMirrorState?> RemainingSkillPointsAsync(
        CancellationToken cancellationToken) =>
        skillPoints.RemainingAsync(characterId, cancellationToken);

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
        //
        // Asked of the presence record, where this used to ask whether a session had published its
        // runtime state. The two say the same thing: entry publishes that state and admits the
        // character to this record in one method with nothing awaited between the two statements,
        // and cleanup withdraws the record and removes that state in one synchronous run. Neither
        // is written anywhere else. What is refused here is the gap entry leaves before both
        // happen -- the session is registered and its lease is being prepared, which is a database
        // round trip a request can arrive during.
        if (presence.Find(characterId) is null)
        {
            return ItemUsability.CharacterStateUnavailable;
        }

        // What the character is, asked of the character. It used to be taken off the session's copy
        // of it, which every path that moves the character has to remember to correct -- and a
        // judgement made against a copy is a judgement made against whatever was last written into
        // it. There is no such thing to be behind now: both facts below are read from the row a
        // change writes to.
        //
        // A character this server does not have is reported as nothing to judge against rather than
        // as the archive refusing the item, because those are different things to tell a player:
        // the archive's own refusal says their potion is not a consumable.
        if (await this.IdentityAsync(cancellationToken).ConfigureAwait(false) is not { } identity)
        {
            return ItemUsability.CharacterStateUnavailable;
        }

        // Read rather than folded into the question above, because they are two questions: what the
        // character is does not say how far along it is, and a level-up moves one without touching
        // the other. A missing row answers the same way here, and for the same reason.
        if (await this.ProgressAsync(cancellationToken).ConfigureAwait(false) is not { } progress)
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
                progress.Level,
                CharacterJobFamilies.Resolve(identity.Job),
                this.Place(),
                coolingDown));
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
    /// <para>
    /// The one caller there is refuses a character with no record of its own before reaching here,
    /// so along that path the default only ever covers a record that says the character is standing
    /// in a town. The missing-record case is written out because it is what this method means, not
    /// because anything reaches it today.
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
