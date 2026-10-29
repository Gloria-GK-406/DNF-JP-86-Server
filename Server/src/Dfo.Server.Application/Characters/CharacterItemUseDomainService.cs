using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// What a character supplies to the item-use judgement: what it is, where it stands, and whether it is
/// waiting on a cooldown. What the judgement does with those is the archive's business and lives in a
/// service that could not find a character if it wanted to.
/// </summary>
/// <remarks>
/// Whether the character is in a dungeon is the participation registry's answer, which ordinary runs and
/// both towers claim.
/// </remarks>
internal sealed class CharacterItemUseDomainService(
    ICharacterDomainService characters,
    IItemUseJudgementService judgement,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    ITownAreaDomainService townAreas,
    IDungeonParticipationDomainService participation) : ICharacterItemUseDomainService
{
    public async ValueTask<ItemUsability> CanUseAsync(
        CharacterId character, int itemId, CancellationToken cancellationToken)
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

        var facts = await FactsAsync(character, judgement.CooldownKeyFor(itemId, rule), cancellationToken)
            .ConfigureAwait(false);
        return facts is null ? ItemUsability.CharacterStateUnavailable : judgement.Judge(rule, facts);
    }

    public async ValueTask<ItemUseCandidate?> FactsAsync(
        CharacterId character, ItemCooldownKey cooldown, CancellationToken cancellationToken)
    {
        var entity = characters.Get(character);
        if (townAreas.Find(character) is null && !InDungeon(character)
            || await entity.IdentityAsync(cancellationToken).ConfigureAwait(false) is not { } identity
            || await entity.ProgressAsync(cancellationToken).ConfigureAwait(false) is not { } progress)
            return null;
        return new(progress.Level, CharacterJobFamilies.Resolve(identity.Job), Place(character),
            onlineCharacters.IsCoolingDown(character, cooldown));
    }

    private bool InDungeon(CharacterId character) => participation.Find(character) is not null;

    /// <summary>
    /// Where the character is, as far as the archive's vocabulary goes.
    /// </summary>
    /// <remarks>
    /// A dungeon is the only other place this server could put anyone, so a character who is playing
    /// and holds no dungeon seat is in a town whether or not anyone recorded them there.
    /// <para>
    /// The default therefore reads as "town" rather than "unknown". That is the permissive direction:
    /// a character inside a dungeon whose membership record is missing would be allowed a village-only
    /// item. The one caller there is refuses a character with no record of its own before reaching
    /// here, so along that path the default only ever covers a record that says the character is
    /// standing in a town.
    /// </para>
    /// </remarks>
    private ItemUsePlace Place(CharacterId character) =>
        InDungeon(character) ? ItemUsePlace.Dungeon : ItemUsePlace.Village;
}
