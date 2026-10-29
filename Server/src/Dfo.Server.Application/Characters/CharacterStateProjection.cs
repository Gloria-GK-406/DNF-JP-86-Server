using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Characters;

/// <summary>Shared projections of character facts, without login placement or recovery.</summary>
internal static class CharacterStateProjection
{
    internal static CharacterEntryState CompleteAppearanceProjection(ActiveCharacterState character, CharacterEntryState entry, CharacterInventoryEntryState inventory)
    {
        var equipment = inventory.Containers.SingleOrDefault(x => x.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        var display = CharacterAppearanceRuleService.Shared.Resolve(
            new(CharacterCareer.FromPacked(character.Identity.Job, character.Identity.GrowType)), equipment,
            new(null, checked((byte)entry.Public.VisibilityBits)));
        return entry with { Public = entry.Public with { Display = display, CloneTitleItemId = display.TitleAppearanceItemId } };
    }

    internal static CharacterSkillsEntryState MergePvpProfile(
        CharacterSkillsEntryState normal, CharacterSkillsEntryState pvp) => normal with
    {
        Pages = pvp.Pages,
        ProtocolMirror = pvp.ProtocolMirror
            ?? throw new InvalidDataException("PvP skill protocol mirror is unavailable."),
    };

    internal static async ValueTask<CharacterRosterEntryState> BuildRosterEntryAsync(
        AccountId accountId,
        AccountCharacterRoster roster,
        CharacterId selectedId,
        CharacterEntryState selectedEntry,
        ICharacterEntryStateDomainService entries,
        CancellationToken cancellationToken)
    {
        var characters = new List<CharacterRosterMemberState>(roster.Characters.Count);
        foreach (var summary in roster.Characters.OrderBy(value => value.SlotIndex))
        {
            var entry = summary.CharacterId == selectedId
                ? selectedEntry
                : await entries.LoadAsync(accountId, summary.CharacterId, cancellationToken).ConfigureAwait(false);
            characters.Add(new CharacterRosterMemberState(
                summary.CharacterId, summary.SlotIndex, summary.Name, summary.Job, summary.GrowType,
                summary.Level,
                checked((uint)Math.Max(0, entry.Public.DerivedStats.Progress1)),
                checked((uint)Math.Max(0, entry.Public.DerivedStats.Progress2)),
                0,
                summary.Appearances ?? throw new InvalidDataException(
                    "The account roster this frame is drawn from did not work out appearances."),
                summary.CloneTitleItemId) { Display = summary.Display });
        }
        return new CharacterRosterEntryState(
            roster.SlotLimit, roster.SlotLimit, checked((byte)selectedEntry.Public.Extras.ManageLevel),
            0, 0, 0, characters);
    }

    internal static CharacterEntryState CompleteCollectionBoxProjection(
        CharacterEntryState entry,
        WorldContentEntryState content,
        DateTimeOffset preparedAt)
    {
        // Content decides which boxes exist; character storage only supplies progress within them.
        // This used to run the other way round -- the entry state invented a fixed pair of box
        // indices and this projection demanded a definition for each -- which made entry fail
        // outright on any archive that did not happen to declare those two. The archive in use
        // declares boxes 1 and 2 while the hard-coded pair was 3 and 4, so it never could have
        // resolved. The reference server derives the set the same way, from its collect-box table.
        var stored = entry.Features.Collections.CollectionBoxes.ToDictionary(value => value.BoxIndex);
        var boxes = content.CollectionBoxes.OrderBy(value => value.BoxIndex).Select(definition =>
        {
            var index = checked((byte)definition.BoxIndex);
            var box = stored.TryGetValue(index, out var progress)
                ? progress
                : new CharacterCollectionBoxProgressState(index, []);

            uint remainingSeconds;
            byte statusFlags;
            if (definition.MaximumExpiresAt is not { } maximum)
            {
                remainingSeconds = 0;
                statusFlags = 1;
            }
            else if (maximum <= preparedAt)
            {
                remainingSeconds = uint.MaxValue;
                statusFlags = 0;
            }
            else
            {
                remainingSeconds = checked((uint)Math.Min(
                    uint.MaxValue,
                    Math.Floor((maximum - preparedAt).TotalSeconds)));
                statusFlags = 0;
            }

            return box with
            {
                Protocol = new CharacterCollectionBoxProtocolState(remainingSeconds, statusFlags),
            };
        }).ToArray();

        return entry with
        {
            Features = entry.Features with
            {
                Collections = entry.Features.Collections with { CollectionBoxes = boxes },
            },
        };
    }

    internal static async ValueTask<CharacterEntryState> CompleteSkillProjectionAsync(
        ActiveCharacterState character,
        CharacterEntryState entry,
        ICharacterEntryPreparationRuleService rules,
        CancellationToken cancellationToken)
    {
        var learnedIds = entry.Skills.Pages.SelectMany(value => value.Skills)
            .Select(value => checked((int)value.SkillId)).Distinct().ToArray();
        var definitions = await rules.SkillPointsAsync(
            character.Identity.Job,
            character.Identity.GrowType,
            character.Progression.Level,
            learnedIds,
            cancellationToken).ConfigureAwait(false);
        // The arithmetic itself belongs to the rule service, which already holds the archive's
        // answers. Entry supplies what it has in hand; a character asked about its points later
        // supplies the same facts read from storage, and both get the one implementation.
        var mirror = rules.RemainingPoints(
            definitions,
            character.Progression.Level,
            entry.Skills.Pages,
            entry.Skills.SkillPointBasis);
        return entry with { Skills = entry.Skills with { ProtocolMirror = mirror } };
    }

    internal static async ValueTask<AccountEntryState> CompleteSupportCharacterProjectionAsync(
        AccountId accountId,
        ActiveCharacterState selected,
        AccountCharacterRoster roster,
        AccountEntryState account,
        ICharacterRosterDomainService characters,
        Func<AccountId, CharacterId, CancellationToken, ValueTask<CharacterInventoryEntryState>> readInventory,
        ICharacterEntryStateDomainService entries,
        ICharacterEntryPreparationRuleService preparationRules,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        var ownerRelations = account.Relations.CharacterRelations
            .Where(value => value.OwnerCharacterId == selected.Identity.CharacterId).ToArray();
        if (ownerRelations.Length == 0)
            return account;
        if (ownerRelations.Length != 1 || ownerRelations[0].Slot != 0)
            throw new InvalidDataException("Select-character supports exactly one owner relation in slot zero.");
        var relation = ownerRelations[0];
        var summary = roster.Characters.SingleOrDefault(
            value => value.CharacterId == relation.SupportCharacterId)
            ?? throw new InvalidDataException("Support character is absent from the account roster.");
        var support = await characters.LoadForSelectionAsync(
            accountId, summary.SlotIndex, cancellationToken).ConfigureAwait(false);
        var supportEntry = await entries.LoadAsync(
            accountId, relation.SupportCharacterId, cancellationToken).ConfigureAwait(false);
        var supportInventory = await readInventory(accountId, relation.SupportCharacterId, cancellationToken)
            .ConfigureAwait(false);
        supportInventory = await SupportCreatureMarkerProjection.CompleteAsync(
            supportInventory, preparationRules, preparedAt, cancellationToken)
            .ConfigureAwait(false);
        supportEntry = CompleteAppearanceProjection(support, supportEntry, supportInventory);
        var completed = relation with
        {
            SupportProjection = new AccountSupportCharacterEntryState(
                support, supportEntry, supportInventory),
        };
        return account with
        {
            Relations = account.Relations with
            {
                CharacterRelations = account.Relations.CharacterRelations
                    .Select(value => value == relation ? completed : value).ToArray(),
            },
        };
    }

    internal static WorldContentSubject BuildContentSubject(
        IEquippedCreatureQuery equippedCreature,
        ActiveCharacterState character,
        CharacterEntryState entry,
        CharacterInventoryEntryState characterInventory)
    {
        var claimedGroups = entry.Progression.DailyChallenges
            .Where(group => group.RewardClaimed)
            .Select(group => checked((int)group.GroupId))
            .ToArray();
        var boxes = entry.Features.Collections.CollectionBoxes
            .Select(box => new CollectionBoxProgressSubject(
                box.BoxIndex,
                box.Slots.Select(slot => slot.ItemId).ToArray()))
            .ToArray();
        var clearedQuestFlags = entry.Quests.VisibilityFlags
            .Select((value, questId) => (questId, value))
            .Where(pair => pair.value != 0)
            .ToDictionary(pair => pair.questId, pair => checked((int)pair.value));

        return new WorldContentSubject(
            character.Progression.Level,
            character.Identity.Job,
            character.Identity.GrowType,
            clearedQuestFlags,
            [.. entry.Quests.ActiveQuests.Select(quest => quest.QuestId)],
            claimedGroups,
            boxes,
            equippedCreature.GetItemId(characterInventory),
            entry.Public.DerivedStats.EquippedCreatureLevel);
    }
}
