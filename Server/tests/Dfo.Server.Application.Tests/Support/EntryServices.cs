using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Game;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Builds a <see cref="SelectCharacterCommandHandler"/> for tests, defaulting every
/// collaborator a test is not interested in.
/// </summary>
/// <remarks>
/// The handler intentionally exposes every participating domain. Funnelling test-only
/// defaults through here keeps unrelated behavior tests focused without hiding the
/// production dependency boundary.
/// </remarks>
internal static class EntryServices
{
    public static SelectCharacterCommandHandler Create(
        ICharacterService characters,
        ITestInventoryEntryServices inventory,
        ICharacterEntryStateService characterEntries,
        IAccountEntryStateService accountEntries,
        IActiveCharacterService activeCharacters,
        IWorldContentEntryService? worldContent = null,
        ISkillPointDefinitionSource? skillPoints = null,
        ICreatureEquipmentLifetimeSource? creatureLifetimes = null,
        IChannelEntryPolicy? channels = null,
        IEntryPlacementService? placement = null,
        ISessionAdjacentEntryService? sessionAdjacent = null,
        TimeProvider? timeProvider = null) =>
        new(characters,
            inventory,
            inventory,
            characterEntries,
            accountEntries,
            new EquippedCreatureQuery(),
            worldContent ?? new StubWorldContentEntryService(),
            skillPoints ?? new StubSkillPointDefinitionSource(),
            creatureLifetimes ?? new StubCreatureEquipmentLifetimeSource(),
            channels ?? new StubChannelEntryPolicy(),
            placement ?? new StubEntryPlacementService(),
            sessionAdjacent ?? new StubSessionAdjacentEntryService(),
            activeCharacters,
            timeProvider ?? TimeProvider.System);

    public static ValueTask<SelectCharacterResult> EnterAsync(
        this SelectCharacterCommandHandler handler,
        AccountId accountId,
        ushort slotIndex,
        ChannelEntryIdentity channel,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(
            new CommandExecutionContext(
                Guid.NewGuid(),
                typeof(SelectCharacterCommand),
                typeof(SelectCharacterCommandHandler),
                CommandExecutionKind.Workflow,
                AccountAccessMode: UserAccessMode.Exclusive),
            new SelectCharacterCommand(accountId, slotIndex, channel),
            cancellationToken);
}

internal sealed class StubCreatureEquipmentLifetimeSource : ICreatureEquipmentLifetimeSource
{
    public ValueTask<CreatureEquipmentLifetime> GetAsync(
        int itemTemplateId, DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(default(CreatureEquipmentLifetime));
}

internal sealed class StubSkillPointDefinitionSource : ISkillPointDefinitionSource
{
    public ValueTask<SkillPointDefinitionSet> GetAsync(
        byte job, byte packedGrowType, byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new SkillPointDefinitionSet(0, 0, [],
            new Dictionary<int, SkillPointCostDefinition>()));
}

internal interface ITestInventoryEntryServices
    : ICharacterInventoryEntryService, IAccountInventoryEntryService;
