using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// Answers whether a character may use an item, from the archive's own declaration.
/// </summary>
/// <remarks>
/// Every fact it needs is fetched here. The alternative -- handing the caller a rule object and
/// letting it compare -- puts the same four comparisons at every call site, and the next call site
/// gets one of them wrong.
///
/// Nothing here writes. Starting the cooldown belongs to the effects, which run only after the use
/// succeeds; a question that started a cooldown would make asking twice a different thing from
/// asking once.
/// </remarks>
internal sealed class ItemUsabilityDomainService(
    IItemUseRuleDefinitionSource rules,
    ICharacterItemCooldownDomainService cooldowns,
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionRuntimeDirectory runtimeSessions,
    ISessionPresenceRegistry presence) : IItemUsabilityDomainService
{
    public async ValueTask<ItemUsability> CanUseAsync(
        CharacterId characterId,
        int itemId,
        CancellationToken cancellationToken)
    {
        var rule = await rules.GetAsync(itemId, cancellationToken).ConfigureAwait(false);

        // An unreadable declaration is refused rather than waved through. The archive names job
        // families and places this contract does not model -- a misspelt [prist], a [seria room] --
        // and reading "restriction I do not understand" as "no restriction" hands out exactly the
        // items the archive meant to withhold.
        if (rule is null || !rule.IsReadable)
            return ItemUsability.NotAUsableItem;

        var character = FindCharacter(characterId);
        if (character is null)
            return ItemUsability.CharacterStateUnavailable;

        // The permanent reasons are asked first. A cooldown expires, so reporting it in front of a
        // level the character has not reached sends the player away to wait for something that will
        // not help -- and cooldowns are shared, so the item that started the wait need not be the
        // item being refused.
        if (character.Progression.Level < rule.MinimumLevel)
            return ItemUsability.LevelTooLow;

        if (!rule.Jobs.Allows(CharacterJobFamilies.Resolve(character.Identity.Job)))
            return ItemUsability.JobMismatch;

        if (!rule.Places.Allows(PlaceOf(characterId)))
            return ItemUsability.PlaceNotAllowed;

        return rule.CooldownMilliseconds > 0
            && cooldowns.IsCoolingDown(characterId, CooldownKeyFor(itemId, rule))
                ? ItemUsability.OnCooldown
                : ItemUsability.Allowed;
    }

    /// <summary>The cooldown this item shares, or its own when it shares none.</summary>
    public static ItemCooldownKey CooldownKeyFor(int itemId, ItemUseRule rule) =>
        rule.CooltimeGroup > 0
            ? ItemCooldownKey.ForGroup(rule.CooltimeGroup)
            : ItemCooldownKey.ForItem(itemId);

    private ActiveCharacterState? FindCharacter(CharacterId characterId)
    {
        var session = selectedSessions.FindByCharacter(characterId);
        if (session is null)
            return null;

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
    private ItemUsePlace PlaceOf(CharacterId characterId) =>
        presence.Find(characterId) is { StandingInTown: false }
            ? ItemUsePlace.Dungeon
            : ItemUsePlace.Village;
}
