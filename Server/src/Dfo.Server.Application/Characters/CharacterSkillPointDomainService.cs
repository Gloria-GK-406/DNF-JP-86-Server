using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// A character's remaining skill points, read from storage and worked out afresh each time.
/// </summary>
/// <remarks>
/// The second supplier of one sum. Entry has the four facts in hand already -- it has just built
/// the character's pages and read its bonus -- and hands them to
/// <see cref="ICharacterEntryPreparationRuleService.RemainingPoints"/>; this reads the same four
/// out of the tables and hands them to the same method. Neither of them owns the arithmetic, so
/// neither can drift from the other.
/// <para>
/// It holds nothing between calls. The whole point of asking a character rather than a session is
/// that the answer is worked out from what is stored at the moment of the question, and a cache
/// here would put the stale figure back one level down.
/// </para>
/// <para>
/// <b>This answers about the ordinary skill tree, and only that one.</b> Entry does not, on one
/// channel: where <c>PvpSkillProfileApplies</c> holds -- the free-duel channel, and only it --
/// <c>SelectCharacterPreparationService</c> swaps the character's skills for the separately stored
/// duel profile, whose mirror is <c>(65535, 65535, 65535, 65535)</c>. That is not a figure but a
/// statement: a duel channel grants points without limit. Nothing here knows about it, so asked on
/// such a channel this answers the PVE ledger, which is not what entry published.
/// </para>
/// <para>
/// Written down rather than handled, because the branch cannot be reached today:
/// <c>ChannelEntryOptions.Legacy</c> sets <c>FreeDuelEnabled: false</c>, so nobody enters on that
/// channel at all, and inventing a duel answer for a path nobody can walk would be inventing
/// behaviour rather than serving it. The day free duel is switched on, this needs an answer of its
/// own: the frames that carry these points would otherwise report a PVE figure where the reference
/// server reports no limit.
/// </para>
/// </remarks>
internal sealed class CharacterSkillPointDomainService(
    ICharacterTable characters,
    ICharacterSkillTable skills,
    ICharacterEntryTable entries,
    ICharacterEntryPreparationRuleService rules) : ICharacterSkillPointDomainService
{
    public async ValueTask<CharacterSkillProtocolMirrorState?> RemainingAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        // What the archive prices by, read first because without it there is nothing to price. A
        // character that is gone answers nothing at all rather than an empty ledger.
        if (await characters.FindStandingAsync(characterId, cancellationToken)
                .ConfigureAwait(false) is not { } standing)
        {
            return null;
        }

        var level = checked((byte)standing.Level);

        // The ordinary pages, which are the ones a skill point is spent on and the ones entry
        // reconciles. The duel pages are a separate set of rows behind a separate read, and
        // pricing against them would answer about a skill tree the player is not spending in.
        var pages = CharacterSkillPages.Pages(CharacterSkillPages.Rows(
            await skills.ReadEntryAsync(characterId, cancellationToken).ConfigureAwait(false)));

        var learnedIds = pages.SelectMany(page => page.Skills)
            .Select(value => checked((int)value.SkillId)).Distinct().ToArray();
        var definitions = await rules.SkillPointsAsync(
            checked((byte)standing.Job),
            checked((byte)standing.GrowType),
            level,
            learnedIds,
            cancellationToken).ConfigureAwait(false);

        // A character with no bonus row has no bonus, which is what entry makes of the same
        // absence. Read after the standing, so a character that has since gone is already refused
        // above rather than priced with a zero bonus.
        var bonuses = await entries.FindBonusPointsAsync(characterId, cancellationToken)
            .ConfigureAwait(false) ?? new BonusPointRow(0, 0);

        return rules.RemainingPoints(
            definitions,
            level,
            pages,
            new CharacterSkillPointBasisState(bonuses.BonusSp, bonuses.BonusTp));
    }
}
