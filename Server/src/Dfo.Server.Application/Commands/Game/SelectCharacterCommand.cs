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
using Dfo.Server.Application.Characters;

namespace Dfo.Server.Application.Commands.Game;

/// <summary>
/// Enter the game with one of an account's characters.
/// </summary>
/// <param name="Channel">
/// The channel the connection arrived on. It is resolved before dispatch because entry
/// policy — admission, spawn, position persistence and duelling applicability — all follow
/// from it, and a command that had to ask which listener it came from would be reaching
/// back into the transport.
/// </param>
public sealed record SelectCharacterCommand(
    AccountId AccountId,
    ushort SlotIndex,
    ChannelEntryIdentity Channel) : ICommand<SelectCharacterResult>, IAccountCommand;

public sealed class SelectCharacterCommandHandler(
    ICharacterService characters,
    ICharacterInventoryEntryService characterInventoryEntries,
    IAccountInventoryEntryService accountInventoryEntries,
    ICharacterEntryStateService characterEntries,
    IAccountEntryStateService accountEntries,
    IEquippedCreatureQuery equippedCreature,
    IWorldContentEntryService worldContent,
    ISkillPointDefinitionSource skillPoints,
    ICreatureEquipmentLifetimeSource creatureLifetimes,
    IChannelEntryPolicy channels,
    IEntryPlacementService placement,
    ISessionAdjacentEntryService sessionAdjacent,
    IActiveCharacterService activeCharacters,
    TimeProvider timeProvider)
    : ICommandHandler<SelectCharacterCommand, SelectCharacterResult>
{
    public async ValueTask<SelectCharacterResult> HandleAsync(
        CommandExecutionContext context,
        SelectCharacterCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Channel);

        var character = await characters.LoadForSelectionAsync(
            command.AccountId,
            command.SlotIndex,
            cancellationToken).ConfigureAwait(false);

        // Admission depends only on minimum character facts. Refuse before loading any
        // entry state so the rejected selection leaves the current online state untouched.
        if (channels.Refuse(command.Channel, character.Progression.Level) is { } refusal)
            throw new ChannelAdmissionRefusedException(command.Channel, refusal);

        var roster = await characters.GetAccountRosterAsync(
            command.AccountId,
            cancellationToken).ConfigureAwait(false);
        if (roster.AccountId != command.AccountId
            || roster.Characters.All(value => value.CharacterId != character.Identity.CharacterId))
        {
            throw new InvalidDataException("Selected character is absent from the account roster.");
        }

        var characterInventory = await characterInventoryEntries.LoadAsync(
            command.AccountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var accountInventory = await accountInventoryEntries.LoadAsync(
            command.AccountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var characterEntry = await characterEntries.LoadAsync(
            command.AccountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var accountEntry = await accountEntries.LoadAsync(
            command.AccountId,
            cancellationToken).ConfigureAwait(false);
        var preparedAt = timeProvider.GetUtcNow();
        accountEntry = await CompleteSupportCharacterProjectionAsync(
            command.AccountId, character, roster, accountEntry,
            characters, characterInventoryEntries, characterEntries,
            creatureLifetimes, preparedAt,
            cancellationToken).ConfigureAwait(false);

        var content = await worldContent.LoadAsync(
            BuildContentSubject(character, characterEntry, characterInventory),
            cancellationToken).ConfigureAwait(false);
        characterEntry = await CompleteSkillProjectionAsync(
            character, characterEntry, skillPoints, cancellationToken).ConfigureAwait(false);
        var channelPolicy = await placement.ResolveAsync(
            command.Channel,
            character.Position.TownId,
            cancellationToken).ConfigureAwait(false);
        var adjacent = await sessionAdjacent.LoadAsync(
            new SessionAdjacentSubject(
                character.Identity.CharacterId,
                command.AccountId,
                command.Channel,
                channelPolicy.Spawn.TownId,
                channelPolicy.Spawn.AreaId),
            cancellationToken).ConfigureAwait(false);

        characterEntry = CompleteCollectionBoxProjection(characterEntry, content, preparedAt);

        var result = new SelectCharacterResult(
            character,
            characterEntry,
            characterInventory,
            accountInventory,
            accountEntry,
            new GameWorldEntryState(preparedAt, content, channelPolicy),
            new CharacterSessionEntryState(
                command.AccountId,
                character.Identity.CharacterId,
                adjacent),
            roster);

        // Publication is the only state change and deliberately remains last. A consumer
        // that sees the new active character is entitled to the complete result above.
        activeCharacters.Publish(command.AccountId, character);
        return result;
    }

    private static CharacterEntryState CompleteCollectionBoxProjection(
        CharacterEntryState entry,
        WorldContentEntryState content,
        DateTimeOffset preparedAt)
    {
        var definitions = content.CollectionBoxes.ToDictionary(value => value.BoxIndex);
        var boxes = entry.Features.Collections.CollectionBoxes.Select(box =>
        {
            if (!definitions.TryGetValue(box.BoxIndex, out var definition))
                throw new InvalidDataException($"Collection-box definition {box.BoxIndex} was not resolved.");

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

    private static async ValueTask<CharacterEntryState> CompleteSkillProjectionAsync(
        ActiveCharacterState character,
        CharacterEntryState entry,
        ISkillPointDefinitionSource source,
        CancellationToken cancellationToken)
    {
        var learnedIds = entry.Skills.Pages.SelectMany(value => value.Skills)
            .Select(value => checked((int)value.SkillId)).Distinct().ToArray();
        var definitions = await source.GetAsync(
            character.Identity.Job,
            character.Identity.GrowType,
            character.Progression.Level,
            learnedIds,
            cancellationToken).ConfigureAwait(false);
        var baseline = definitions.FreeGrants.GroupBy(value => value.SkillId)
            .ToDictionary(group => group.Key, group => group.Max(value => value.Level));

        ushort RemainingFor(byte pageIndex, bool tp)
        {
            var spent = 0;
            var page = entry.Skills.Pages.SingleOrDefault(value => value.PageIndex == pageIndex);
            foreach (var learned in page?.Skills ?? [])
            {
                if (!definitions.Skills.TryGetValue(checked((int)learned.SkillId), out var skill))
                    continue; // Legacy treats an absent PVF skill definition as unchargeable.
                baseline.TryGetValue(skill.SkillId, out var freeLevel);
                if (skill.IsFixedLevel && character.Progression.Level >= skill.RequiredLevel)
                {
                    var fixedLevel = skill.FixedLevelBase
                        + (character.Progression.Level - skill.RequiredLevel) / Math.Max(1, skill.FixedLevelInterval)
                        * skill.FixedLevelAddPerInterval;
                    freeLevel = checked((byte)Math.Min(byte.MaxValue,
                        Math.Max(freeLevel, Math.Min(fixedLevel, skill.MaximumLevel))));
                }
                if (skill.UsesTp != tp || learned.Level <= freeLevel || skill.Costs.Count == 0)
                    continue;
                for (var level = freeLevel; level < learned.Level; level++)
                    spent = checked(spent + skill.Costs[Math.Min(level, skill.Costs.Count - 1)]);
            }
            var total = (tp ? definitions.TotalTp : definitions.TotalSp)
                + (tp ? entry.Skills.SkillPointBasis.BonusTp : entry.Skills.SkillPointBasis.BonusSp);
            return checked((ushort)Math.Clamp(total - spent, 0, ushort.MaxValue));
        }

        var mirror = new CharacterSkillProtocolMirrorState(
            RemainingFor(0, false), RemainingFor(1, false),
            RemainingFor(0, true), RemainingFor(1, true));
        return entry with { Skills = entry.Skills with { ProtocolMirror = mirror } };
    }

    private static async ValueTask<AccountEntryState> CompleteSupportCharacterProjectionAsync(
        AccountId accountId,
        ActiveCharacterState selected,
        AccountCharacterRoster roster,
        AccountEntryState account,
        ICharacterService characters,
        ICharacterInventoryEntryService inventories,
        ICharacterEntryStateService entries,
        ICreatureEquipmentLifetimeSource creatureLifetimes,
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
        var supportInventory = await inventories.LoadAsync(
            accountId, relation.SupportCharacterId, cancellationToken).ConfigureAwait(false);
        supportInventory = await CompleteSupportCreatureMarkerAsync(
            supportInventory, creatureLifetimes, preparedAt, cancellationToken)
            .ConfigureAwait(false);
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

    private static async ValueTask<CharacterInventoryEntryState> CompleteSupportCreatureMarkerAsync(
        CharacterInventoryEntryState inventory,
        ICreatureEquipmentLifetimeSource lifetimes,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        var containers = new List<InventoryContainerState>(inventory.Containers.Count);
        foreach (var container in inventory.Containers)
        {
            if (container.ContainerType != InventoryContainerType.Equipment)
            {
                containers.Add(container);
                continue;
            }

            var items = new List<InventoryItemState>(container.Items.Count);
            foreach (var item in container.Items)
            {
                if (item.SlotIndex != 24 || item.Core.ItemKind != 5)
                {
                    items.Add(item);
                    continue;
                }

                var lifetime = await lifetimes.GetAsync(
                    item.Core.ItemId, preparedAt, cancellationToken).ConfigureAwait(false);
                var marker = SupportCreatureMarkerProjection.Resolve(
                    lifetime, item.Core.ExpireTime, item.Core.Marker16, preparedAt);
                items.Add(item with { Noti2CreatureMarker = marker });
            }
            containers.Add(container with { Items = items });
        }
        return inventory with { Containers = containers };
    }

    private WorldContentSubject BuildContentSubject(
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
            claimedGroups,
            boxes,
            equippedCreature.GetItemId(characterInventory),
            entry.Public.Subtype1.EquippedCreatureLevel);
    }
}
