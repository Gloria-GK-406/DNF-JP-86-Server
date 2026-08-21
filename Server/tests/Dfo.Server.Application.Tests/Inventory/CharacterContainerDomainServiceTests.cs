using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.UseStackable;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Which containers a character holds, and which one of them its items come from.
/// </summary>
/// <remarks>
/// The binding is what makes a second inventory possible at all: a dungeon registers its own
/// container and switches to it, and every rule about moving and using items carries on without
/// knowing a switch happened.
/// </remarks>
public sealed class CharacterContainerDomainServiceTests
{
    private static readonly CharacterId Character = new(60);

    [Fact]
    public void The_first_container_registered_is_the_one_in_use()
    {
        var containers = new CharacterContainerDomainService();
        var first = Container("first");
        var second = Container("second");

        containers.Register(Character, first);
        containers.Register(Character, second);

        Assert.Equal(
            [first, second], containers.GetContainers(Character, InventoryContainerType.Main));
        Assert.Same(first, containers.GetContainer(Character, InventoryContainerType.Main));
    }

    [Fact]
    public void Switching_changes_the_binding_and_nothing_else()
    {
        var containers = new CharacterContainerDomainService();
        var first = Container("first");
        var second = Container("second");
        containers.Register(Character, first);
        containers.Register(Character, second);

        Assert.True(containers.UseContainer(Character, second.Id));

        Assert.Same(second, containers.GetContainer(Character, InventoryContainerType.Main));
        Assert.Equal(
            [first, second], containers.GetContainers(Character, InventoryContainerType.Main));
    }

    /// <remarks>
    /// Leaving a dungeon, in one call: the run's container goes and the character is back on its
    /// own without anything having to name which container that was.
    /// </remarks>
    [Fact]
    public void Removing_the_container_in_use_falls_back_to_the_one_that_cannot_be_removed()
    {
        var containers = new CharacterContainerDomainService();
        var own = Container("own", removable: false);
        var dungeon = Container("dungeon");
        containers.Register(Character, own);
        containers.Register(Character, dungeon);
        Assert.True(containers.UseContainer(Character, dungeon.Id));

        Assert.True(containers.Remove(Character, dungeon.Id));

        Assert.Same(own, containers.GetContainer(Character, InventoryContainerType.Main));
        Assert.Equal([own], containers.GetContainers(Character, InventoryContainerType.Main));
    }

    /// <remarks>
    /// A character bound to nothing has no way back, so this is raised rather than represented --
    /// and raised before anything is removed, so the character is left exactly as it was.
    /// </remarks>
    [Fact]
    public void Removing_the_only_container_leaves_the_character_bound_where_it_was()
    {
        var containers = new CharacterContainerDomainService();
        var only = Container("only");
        containers.Register(Character, only);

        Assert.Throws<InvalidOperationException>(() => containers.Remove(Character, only.Id));

        Assert.Same(only, containers.GetContainer(Character, InventoryContainerType.Main));
        Assert.Equal([only], containers.GetContainers(Character, InventoryContainerType.Main));
    }

    /// <remarks>
    /// The two protocols that read and write items name no storage of their own. That is the whole
    /// of the seam: what a dungeon replaces is the container, and these keep working across it.
    /// <para>
    /// The container is now held one layer down, by the command each protocol dispatches. The
    /// protocol handlers name neither storage nor container -- they turn a frame into a command and
    /// the answer into a code -- which is what makes a refusal reason something the command decides
    /// rather than something the acknowledgement invents.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_item_commands_reach_storage_only_through_a_container()
    {
        foreach (var handler in
                 new[] { typeof(MoveItemSpaceCommandHandler), typeof(UseStackableCommandHandler) })
        {
            var parameters = handler.GetConstructors().Single().GetParameters();
            Assert.DoesNotContain(
                parameters, value => value.ParameterType == typeof(IEntryInventoryMutationRepository));
            Assert.Contains(
                parameters, value => value.ParameterType == typeof(ICharacterContainerDomainService));
        }
    }

    [Fact]
    public void The_item_protocol_handlers_decide_nothing_about_items()
    {
        foreach (var handler in new[] { typeof(MoveItemSpaceHandler), typeof(UseStackableHandler) })
        {
            var parameters = handler.GetConstructors().Single().GetParameters();
            Assert.DoesNotContain(
                parameters, value => value.ParameterType == typeof(IEntryInventoryMutationRepository));
            Assert.DoesNotContain(
                parameters, value => value.ParameterType == typeof(ICharacterContainerDomainService));
            Assert.Contains(parameters, value => value.ParameterType == typeof(ICommandBus));
        }
    }

    private static IItemContainer Container(string id, bool removable = true) =>
        new StandInContainer(
            new ContainerId(id), new ContainerShape(InventoryContainerType.Main, 0), removable);

    /// <summary>
    /// A container that answers for itself and nothing more.
    /// </summary>
    /// <remarks>
    /// What is under test here is the directory that holds containers -- which one is in use, what
    /// happens when a second is registered, what may be removed. None of that asks a container to
    /// hold anything, so this one does not.
    /// </remarks>
    private sealed class StandInContainer(ContainerId id, ContainerShape shape, bool removable)
        : IItemContainer
    {
        public ContainerId Id => id;

        public InventoryContainerType Type => shape.ContainerType;

        public ContainerShape Shape => shape;

        public bool IsRemovable => removable;

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ContainerSlotItem?>(null);

        public ValueTask<ContainerSlotPair> ReadSlotsAsync(
            int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ContainerSlotPair(null, null));

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<InventoryItemState>>([]);

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ContainerSlotItem>>([]);

        public ValueTask PutAsync(
            int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
