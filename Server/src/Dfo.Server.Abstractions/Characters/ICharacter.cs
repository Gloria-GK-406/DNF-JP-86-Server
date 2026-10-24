using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// One character, as something to ask rather than an identifier to look up.
/// </summary>
/// <remarks>
/// It is bound to one character, so its methods do not take one. It holds nothing: every answer is
/// read when it is asked for, which is what keeps it from disagreeing with a use case that rolled
/// back.
///
/// It is an entry point, not a container. What belongs here is what belongs to the character's own
/// body; bags and quest logs have their own repositories. Learned skill IDs and levels belong to
/// the character, while page slot/extra configuration remains on the page projection.
///
/// The gold it carries belongs here by that same test, and the test is about meaning rather than
/// storage. The client draws that gold as an item -- the first virtual slot of the main list, item
/// id zero -- because its inventory window renders a currency with the widget it renders a sword
/// with. That is an encoding at the boundary. What the gold actually is, is a figure on the
/// character: it has none of an item's thirty fields but one, obeys a carry cap no item obeys, and
/// cannot be moved, split, merged, locked or rented. The bag is still reached through its own
/// repository; what is reached here is the character's own money.
///
/// Stored identity, progress, base stats and learned skills are available by ID without a playing
/// session. Runtime-dependent questions still use their owning runtime services.
/// </remarks>
public interface ICharacter
{
    CharacterId Id { get; }

    async ValueTask<CharacterAvatarCandidates?> GetAvatarCandidatesAsync(CancellationToken ct) =>
        await IdentityAsync(ct).ConfigureAwait(false) is { } identity
            ? new(CharacterCareer.FromPacked(identity.Job, identity.GrowType)) : null;

    async ValueTask<CharacterCareer?> GetDefaultAvatarAsync(CancellationToken ct) =>
        (await GetAvatarCandidatesAsync(ct).ConfigureAwait(false))?.Default;

    async ValueTask<CharacterCareer?> GetCareerAvatarAsync(CancellationToken ct) =>
        (await GetAvatarCandidatesAsync(ct).ConfigureAwait(false))?.Career;

    async ValueTask<CharacterCareer?> GetAwakeningAvatarAsync(CancellationToken ct) =>
        (await GetAvatarCandidatesAsync(ct).ConfigureAwait(false))?.Awakening;

    /// <summary>Price and settle a reward in the caller's transaction, including max-level overflow.</summary>
    ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source, DateTimeOffset at,
        Dfo.Server.Data.World.SelectedSessionIdentity? session, IEffectFootprintRecorder footprint, CancellationToken ct);
    /// <summary>Settle an already priced reward; an expected state rejects stale quest decisions.</summary>
    ValueTask<ExperienceAward> AddExpAsync(ExperienceBreakdown reward, CharacterProgressionState? expected, CancellationToken ct);
    /// <summary>Clamp experience and derive level. This is a setter, so it never fills a capsule.</summary>
    ValueTask<CharacterProgressionState> SetExpAsync(long experience, CancellationToken ct);
    /// <summary>Set the cumulative starting experience for the requested playable level.</summary>
    ValueTask<CharacterProgressionState> SetLevelAsync(byte level, CancellationToken ct);
    /// <summary>Apply a guarded experience setter during recovery, without generating a reward.</summary>
    ValueTask<bool> TrySetExpAsync(CharacterProgressionState expected, long experience, CancellationToken ct);


    ValueTask<CharacterBaseStats?> BaseStatsAsync(CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CharacterLearnedSkillPage>?> LearnedSkillsAsync(CancellationToken cancellationToken);
    ValueTask<CharacterSkillPointLedger?> SkillPointLedgerAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether this character may use the item, from the archive's own declaration.
    /// </summary>
    /// <remarks>
    /// A question, and only a question: nothing here starts a cooldown, so asking twice is the same
    /// as asking once. Starting one belongs to the effects, which run after a use succeeds.
    /// </remarks>
    ValueTask<ItemUsability> CanUseAsync(int itemId, CancellationToken cancellationToken);

    ValueTask<ItemUseCandidate?> ItemUseFactsAsync(ItemCooldownKey cooldown, CancellationToken cancellationToken);

    /// <summary>
    /// Which slot this character sits in, what it is called, what it took up and how it grew.
    /// </summary>
    /// <remarks>
    /// Read when asked, like every other answer here, so it is what the store says now rather than
    /// what a session was published with. These four were only reachable through that session's
    /// copy of the character until now, which meant anything holding a character's own identifier
    /// had to find a session before it could ask what the character was -- and got told whatever
    /// was last written into that copy.
    /// <para>
    /// Nothing when there is no such character, for the reason <see cref="ProgressAsync"/> gives:
    /// slot zero with an empty name is a standing a real character can have, so inventing it here
    /// would be indistinguishable from the truth about somebody else.
    /// </para>
    /// </remarks>
    ValueTask<CharacterIdentityState?> IdentityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What level this character has reached and what it holds towards the next one.
    /// </summary>
    /// <remarks>
    /// Read when asked, so it is what the store says now rather than what a session was published
    /// with. Nothing when there is no such character: a placeholder level of one holding nothing
    /// is a real standing that a real character can have, so inventing it here would be
    /// indistinguishable from the truth about somebody else.
    /// </remarks>
    ValueTask<CharacterProgressionState?> ProgressAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What this character has left to spend on skills, on each of its two pages.
    /// </summary>
    /// <remarks>
    /// Worked out at the moment of the question, from the level the store holds now. That is the
    /// whole of it: the figure is not stored anywhere, so an answer kept from an earlier moment is
    /// an answer for an earlier level -- which is exactly what a levelling character was being
    /// told until they logged out and back in.
    /// <para>
    /// Nothing when there is no such character, for the reason <see cref="ProgressAsync"/> gives:
    /// a character who has spent every point also has none left.
    /// </para>
    /// </remarks>
    ValueTask<CharacterSkillProtocolMirrorState?> RemainingSkillPointsAsync(
        CancellationToken cancellationToken);

    /// <summary>How much gold this character is carrying.</summary>
    /// <remarks>
    /// Read when asked, like every other answer here. A character this server does not have
    /// carries nothing, which is the same figure a character who has spent everything carries --
    /// the difference matters to the two members below and not to this one.
    /// </remarks>
    ValueTask<int> GoldAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gives this character that much gold and answers what it carries afterwards.
    /// </summary>
    /// <remarks>
    /// <b>The one way gold arrives.</b> The carry cap is applied here, so an arrival that would
    /// take the character past it fills to the cap and no further. Anything that reached the store
    /// around this would be gold the cap never saw, which is exactly how the reference server ended
    /// up with three different answers to a character carrying all it may.
    /// <para>
    /// A negative amount takes gold away and is held at zero. It is not a way to charge: a charge
    /// that cannot be met has to be refused rather than clamped, which is what
    /// <see cref="TrySpendGoldAsync"/> is for.
    /// </para>
    /// <para>
    /// Answers what the character carries afterwards, which a caller can compare with what it
    /// offered to see whether the cap took a bite.
    /// </para>
    /// </remarks>
    ValueTask<int> AddGoldAsync(int amount, CancellationToken cancellationToken);

    /// <summary>
    /// Takes that much gold from this character, or takes nothing at all.
    /// </summary>
    /// <remarks>
    /// Answers what is carried afterwards, and <c>null</c> when the character was not carrying that
    /// much -- in which case nothing was taken and the figure stands where it stood. Refusing is
    /// the point: a charge clamped to zero would record a player who cannot afford something as
    /// having bought it, and the gold they did not have would be gone as well.
    /// </remarks>
    ValueTask<int?> TrySpendGoldAsync(int amount, CancellationToken cancellationToken);
}

/// <summary>
/// What a character is and where it stands, in the terms the archive's declaration is written in.
/// </summary>
/// <remarks>
/// The judgement takes this rather than a character, which is the whole point of separating them:
/// the four comparisons can then be made without anything that knows how to find a character, and
/// checked without constructing one.
/// </remarks>
public sealed record ItemUseCandidate(
    byte Level,
    QuestJobRequirement JobFamily,
    ItemUsePlace Place,
    bool IsCoolingDown);

/// <summary>
/// Judges the archive's declaration against a candidate. Knows how to find nothing.
/// </summary>
public interface IItemUseJudgementService
{
    /// <summary>
    /// What the archive declares about using this item, or null when it declares nothing usable
    /// under that identifier.
    /// </summary>
    /// <remarks>
    /// Read here rather than by each caller, so that the declaration a judgement is made against
    /// is the same one the effects are planned from.
    /// </remarks>
    ValueTask<ItemUseRule?> RuleForAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>The cooldown this item shares, or its own when it shares none.</summary>
    ItemCooldownKey CooldownKeyFor(int itemId, ItemUseRule rule);

    /// <summary>
    /// Which of the declared restrictions this candidate falls foul of, or that it falls foul of
    /// none.
    /// </summary>
    /// <remarks>
    /// The order is the answer as much as the comparisons are. The permanent reasons are asked
    /// first: a cooldown expires and a level does not, so reporting the cooldown in front of a
    /// level the character has not reached sends the player away to wait for something that will
    /// not help them -- and cooldowns are shared, so the item that started the wait need not be the
    /// item being refused.
    /// </remarks>
    ItemUsability Judge(ItemUseRule rule, ItemUseCandidate candidate);
}
