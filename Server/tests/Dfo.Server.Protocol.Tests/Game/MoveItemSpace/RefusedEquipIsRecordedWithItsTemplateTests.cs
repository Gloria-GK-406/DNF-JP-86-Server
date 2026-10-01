using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

/// <summary>
/// What the log says when the server will not put a piece on, and why the avatar case is read
/// off it.
/// </summary>
/// <remarks>
/// <c>PvfEquipmentSlots.ByName</c> is written out one name at a time, and until 2026-09-08 none of
/// the names in it ended in <c>avatar]</c>. Template 101500690, which the archive declares under
/// <c>[coat avatar]</c>, was run through this path and observed getting nothing back from
/// <c>PvfEquipmentSlotDefinitionSource</c> and being refused by
/// <c>MoveItemSpaceCommandHandler.PutOnAsync</c> as <c>NotWearable</c>. What the request had
/// written in its destination end came back out of the log beside the template number, and that
/// pair is what eleven costume slots were then measured with.
/// <para>
/// <b>The names are in the table now, so this file no longer reads a slot map off a refusal.</b>
/// A costume the archive does have a slot for is refused for aiming at the wrong one, which is the
/// ordinary <c>ItemCannotOccupySlot</c>; <c>NotWearable</c> is left for a template the archive
/// names no slot for at all. The pair the cases hold still is unchanged -- a refusal decided with
/// the piece already read names the piece -- and that is what they are for now.
/// </para>
/// <para>
/// Why that pair is worth holding is what <c>PutOnAsync</c> already says of itself: "A stock
/// client works it out for itself and will not offer a drop it believes is illegal, so a request
/// naming the wrong slot cannot be produced by playing; which is precisely why the server may not
/// take the request's word for it." The server not taking its word for it is one thing; the
/// request still carrying it is what makes the refusal readable.
/// </para>
/// <para>
/// The archive is the real one. The premise these rest on -- what this template is, and which slot
/// the archive gives it -- is asserted rather than assumed, because a double saying so would keep
/// saying so after the archive stopped. That is exactly what happened on 2026-09-08: the premise
/// went from "no worn slot" to "slot three", and the assertion is what said so.
/// </para>
/// <para>
/// <b>These tests do not establish which slot any avatar name belongs in.</b> They establish that
/// two numbers reach the record for the drags exercised here. Whether a live client names the
/// slots these tests use, and what it names for the other ten avatar types, is not something a
/// test can be asked.
/// </para>
/// </remarks>
public sealed class RefusedEquipIsRecordedWithItsTemplateTests(ArchiveContentFixture archive)
    : IClassFixture<ArchiveContentFixture>
{
    /// <summary>
    /// A template the archive declares under <c>[coat avatar]</c>.
    /// </summary>
    /// <remarks>
    /// The first of the three lowest identifiers that name reaches in
    /// <c>equipment/equipment.lst</c>: 101500690, 101500691 and 101500692. One is enough here, and
    /// the two facts this test needs of it are asserted below rather than taken on trust.
    /// </remarks>
    private const int CoatAvatarTemplate = 101500690;

    /// <summary>A template the archive declares under <c>[coat]</c>, which is slot 14.</summary>
    private const int OrdinaryCoatTemplate = 10018;

    /// <summary>Where the piece sits in the bag before the drag.</summary>
    private const short BagSlot = 9;

    /// <summary>The worn slot the request names for the avatar.</summary>
    /// <remarks>
    /// Two, and nothing here says two is where a coat avatar belongs -- that is the number this
    /// record exists to collect, and the server has no opinion about it. What the test asserts is
    /// that whatever the client wrote comes back out of the log.
    /// </remarks>
    private const short NamedAvatarSlot = 2;

    /// <summary>Where the archive puts a coat avatar, measured against a live client.</summary>
    private const byte CoatAvatarSlot = 3;

    /// <summary>A worn slot the ordinary coat does not belong in; the archive says 14.</summary>
    private const short WrongSlotForACoat = 15;

    private static readonly NetworkConnectionId Connection = new("game-avatar-refusal");

    private static readonly NetworkDispatchContext Context = new(
        new NetworkConnectionContext(Connection, NetworkEndpointKind.Game, 10011));

    /// <summary>
    /// The premise: this template is an avatar, and the archive names the slot it is worn in.
    /// </summary>
    /// <remarks>
    /// Both halves matter, and the second one is the half that changed. It used to be that the
    /// archive named <b>no</b> slot for any costume, which is what made the refusal below the only
    /// place a template number could be read from. The names went in on 2026-09-08 and this half
    /// now says which slot -- three -- so the cases beneath it are about a piece aimed at the wrong
    /// slot rather than a piece with no slot at all.
    /// <para>
    /// This asserted that the archive named <b>no</b> worn slot for a coat avatar, which was true
    /// while <c>PvfEquipmentSlots.ByName</c> had no costume names in it and was the whole reason a
    /// costume could not be worn. The names went in on 2026-09-08, measured against a live client,
    /// so the same question now has an answer: three. What the case is for is unchanged -- the
    /// archive's own reading of this template -- and it is worth keeping because the slot it gives
    /// back is one of the eleven that were measured rather than derived.
    /// </para>
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task The_archive_calls_this_template_an_avatar_and_names_its_worn_slot()
    {
        var placement = await archive.Placements.GetAsync(
            CoatAvatarTemplate, CancellationToken.None);

        Assert.NotNull(placement);
        Assert.Equal(ItemWorldClass.Avatar, placement.WorldClass);
        Assert.Equal(
            new BusinessSlot(BusinessInventorySpace.WornAvatar, CoatAvatarSlot),
            await archive.EquipmentSlots.GetAsync(CoatAvatarTemplate, CancellationToken.None));
    }

    /// <summary>
    /// An avatar aimed at a costume slot that is not its own leaves its template and that slot.
    /// </summary>
    /// <remarks>
    /// <b>This was the instrument.</b> It used to drag a coat avatar at slot 2 and record
    /// <c>NotWearable</c>, because the archive named no slot for any costume and the refusal was
    /// the only place a template number could be read from. That is what the eleven slots were
    /// measured with, and the measuring is done: the coat avatar's slot is three, so aiming it at
    /// two is now the ordinary "wrong slot" refusal.
    /// <para>
    /// The case is kept rather than deleted because the property it asserts outlived its purpose:
    /// a refusal decided with the piece already read names the piece. Two is still the slot the
    /// request wrote, and the line still says so.
    /// </para>
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task An_avatar_aimed_at_the_wrong_costume_slot_is_recorded_with_its_template_and_that_slot()
    {
        var (handler, sender, logger) = this.Build(CoatAvatarTemplate);

        await handler.HandleAsync(
            Context, Request(CoatAvatarTemplate, NamedAvatarSlot), CancellationToken.None);

        Assert.Equal(
            $"Move item space game-avatar-refusal character=60: item {CoatAvatarTemplate} "
                + $"from list {(byte)InventoryContainerType.Main} slot {BagSlot} "
                + $"to list {(byte)InventoryContainerType.Equipment} slot {NamedAvatarSlot} "
                + "refused as ItemCannotOccupySlot",
            Assert.Single(logger.Entries).Message);
        Assert.Equal(
            MoveItemSpaceResponse.NotMovable,
            ((MoveItemSpaceResponse)sender.Sent.Single()).ErrorCode);
    }

    /// <summary>
    /// The other refusal decided with the piece in hand names its template too.
    /// </summary>
    /// <remarks>
    /// Template 10018, which the archive declares under <c>[coat]</c>, aimed at 15 rather than the
    /// 14 <c>PvfEquipmentSlots.ByName</c> gives that name. It is the second of the two refusals
    /// <c>PutOnAsync</c> decides with the piece already read, and it is here because the same
    /// change touched both: were only the first passing the identifier on, this line would come
    /// out reading <c>item 0</c>.
    /// </remarks>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot()
    {
        var (handler, _, logger) = this.Build(OrdinaryCoatTemplate);

        await handler.HandleAsync(
            Context, Request(OrdinaryCoatTemplate, WrongSlotForACoat), CancellationToken.None);

        Assert.Equal(
            $"Move item space game-avatar-refusal character=60: item {OrdinaryCoatTemplate} "
                + $"from list {(byte)InventoryContainerType.Main} slot {BagSlot} "
                + $"to list {(byte)InventoryContainerType.Equipment} slot {WrongSlotForACoat} "
                + "refused as ItemCannotOccupySlot",
            Assert.Single(logger.Entries).Message);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Putting_on_a_piece_refuses_a_displaced_item_that_cannot_return_to_the_source_band()
    {
        var (handler, sender, _) = this.Build(OrdinaryCoatTemplate, CoatAvatarTemplate,
            out var bag, out var worn);

        await handler.HandleAsync(Context, Request(OrdinaryCoatTemplate, 14), CancellationToken.None);

        Assert.Equal(MoveItemSpaceResponse.NotMovable,
            Assert.IsType<MoveItemSpaceResponse>(Assert.Single(sender.Sent)).ErrorCode);
        Assert.Equal(OrdinaryCoatTemplate, (await bag.ReadSlotAsync(BagSlot, default))!.ItemId);
        Assert.Equal(CoatAvatarTemplate, (await worn.ReadSlotAsync(14, default))!.ItemId);
    }

    /// <summary>The drag: out of the bag, onto the worn list, at the slot the client named.</summary>
    private static MoveItemSpaceRequest Request(int itemId, short wornSlot) =>
        new(GameMessageMetadata.Empty,
            new GameSlot(InventoryContainerType.Main, BagSlot),
            itemId,
            MoveCount: 0,
            new GameSlot(InventoryContainerType.Equipment, wornSlot),
            DestinationInstanceValue: 0);

    private (MoveItemSpaceHandler Handler, RecordingSender Sender, RecordingLogger Logger) Build(
        int itemIdInTheBag) => this.Build(itemIdInTheBag, null, out _, out _);

    private (MoveItemSpaceHandler Handler, RecordingSender Sender, RecordingLogger Logger) Build(
        int itemIdInTheBag, int? displacedItemId, out SlotDictionaryContainer bag,
        out SlotDictionaryContainer worn)
    {
        var character = new CharacterId(60);
        var containers = new CharacterContainerDomainService();
        bag = new SlotDictionaryContainer(InventoryContainerType.Main);
        bag.Seed(BagSlot, itemIdInTheBag);
        containers.Register(character, bag);
        worn = new SlotDictionaryContainer(InventoryContainerType.Equipment);
        if (displacedItemId is { } item) worn.Seed(14, item);
        containers.Register(character, worn);

        var mappings = new InventorySlotMappingRuleService();
        var commandHandler = new MoveItemSpaceCommandHandler(
            new BusinessInventorySlotDomainService(containers, mappings),
            new ItemMovementDomainService(
                new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                new ItemSlotRuleService(), new RefusedEquipIsRecordedWithItsTemplateTestsWearEligibility()));

        var sender = new RecordingSender();
        var logger = new RecordingLogger();
        return (
            new MoveItemSpaceHandler(
                new OneSession(), new NoPublishedRuntime(),
                mappings,
                new DirectMoveBus(commandHandler), sender, logger),
            sender,
            logger);
    }

    /// <summary>
    /// A container that is a dictionary of slots and refuses everything else.
    /// </summary>
    /// <remarks>
    /// The refusals under test are decided from one read of one slot, so what stands behind that
    /// read does not have to keep items or settle anything. The unused members raise rather than
    /// answering emptily, so a path that started using one of them fails here instead of quietly
    /// being told the container is empty.
    /// </remarks>
    private sealed class SlotDictionaryContainer(InventoryContainerType type) : IItemContainer
    {
        private readonly Dictionary<int, ContainerSlotItem> bySlot = [];

        public ContainerId Id { get; } = ContainerId.ForCharacter(new CharacterId(60), type);

        public InventoryContainerType Type => type;

        public ContainerShape Shape { get; } = new(type, 0);

        public bool IsRemovable => false;

        /// <summary>Puts one of a template in a slot.</summary>
        /// <remarks>
        /// The kind byte and the world class written here are not read on the path under test:
        /// <c>PutOnAsync</c> takes the identifier off the slot and asks the archive, which is the
        /// reading the classification exists to be. They are filled in because
        /// <see cref="ContainerSlotItem"/> carries them, not because either is being described.
        /// </remarks>
        public void Seed(int slotIndex, int itemId) =>
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex,
                StackableItemCore.Create(1, itemId, 1),
                ItemWorldClass.Equipment);

        public ValueTask<ContainerSlotItem?> ReadSlotAsync(
            int slotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(this.bySlot.GetValueOrDefault(slotIndex));

        public ValueTask<ContainerSlotPair> ReadSlotsAsync(
            int firstSlotIndex, int secondSlotIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ContainerSlotPair(
                this.bySlot.GetValueOrDefault(firstSlotIndex),
                this.bySlot.GetValueOrDefault(secondSlotIndex)));

        public ValueTask PutAsync(
            int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken)
        {
            this.bySlot[slotIndex] = new ContainerSlotItem(
                slotIndex, core, ItemWorldClass.Equipment);
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken)
        {
            this.bySlot.Remove(slotIndex);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(
            CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> AddAsync(
            int itemId, int count, CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemPlacementOutcome> RefillAsync(
            ItemRefill refill, CancellationToken cancellationToken) => throw Unused();

        public ValueTask<ItemMoveOutcome> MoveToAsync(
            int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken) =>
            throw Unused();

        private static NotSupportedException Unused() =>
            new("This container answers slot reads and writes and nothing else.");
    }

    /// <summary>A bus that runs the move command on the handler that owns it.</summary>
    /// <remarks>
    /// No use case boundary is opened, because the container behind this one keeps nothing past
    /// the test and so has none to refuse a write outside of. What the boundary is for is checked
    /// where a persistent container is.
    /// </remarks>
    private sealed class DirectMoveBus(
        ICommandHandler<MoveItemSpaceCommand, ItemMoveResult> move) : ICommandBus
    {
        public async ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var context = new CommandExecutionContext(
                Guid.Empty, command.GetType(), typeof(DirectMoveBus),
                CommandExecutionKind.Transactional);
            var result = await move.HandleAsync(
                context, (MoveItemSpaceCommand)(object)command, cancellationToken);
            return (TResult)(object)result;
        }
    }

    private sealed class OneSession : ISelectedSessionDomainService
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(60), Connection, 1, 10011);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => null;

        public bool ReleaseExact(SelectedSessionIdentity session) => false;

        public SelectedSessionIdentity? Find(AccountId accountId) => Session;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            Session;
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The move acknowledgement is sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The move acknowledgement is a CMD.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }


    /// <summary>A session directory that has nothing published, so no appearance frame is built.</summary>
    /// <remarks>
    /// These cases are about the acknowledgement and the log line. The frame that follows a move
    /// which changed the character's look is asserted where it belongs, and it is skipped here
    /// rather than mocked: a session that cannot be read is exactly the case the handler answers by
    /// sending the acknowledgement alone.
    /// </remarks>
    private sealed class NoPublishedRuntime : ILegacySessionSnapshotFixture
    {
        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool shouldPersistPosition) =>
            throw new NotSupportedException("Nothing publishes here.");

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            throw new NotSupportedException("Nothing replaces here.");

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
            throw new NotSupportedException("Nothing is published here.");

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = null;
            shouldPersistPosition = false;
            return false;
        }

        public bool RemoveExact(SelectedSessionIdentity session) => false;
    }

    /// <summary>An inventory loader nobody reaches, because no session is published.</summary>
    private sealed class UnreachableInventory : ICharacterInventoryEntryDomainService
    {
        public ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) => LoadAsync(accountId, characterId, cancellationToken);
        public ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "The inventory is loaded only for a session that is published.");
    }

    /// <summary>The log, kept as the sentences it would have written.</summary>
    private sealed class RecordingLogger : ILogger<MoveItemSpaceHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            this.Entries.Add((logLevel, formatter(state, exception)));
    }
}

/// <summary>
/// The operator's own archive, opened once for the class that reads it.
/// </summary>
/// <remarks>
/// Opening it costs a second and indexing <c>equipment/equipment.lst</c> costs more, and three
/// tests asking the same two questions have no reason to pay for it three times. The production
/// registration is used rather than the sources constructed by hand, so what these tests read is
/// what the server reads.
/// </remarks>
public sealed class ArchiveContentFixture : IDisposable
{
    private readonly ServiceProvider services;

    public ArchiveContentFixture()
    {
        var collection = new ServiceCollection();
        new InfrastructureModule().ConfigureServices(
            collection, new ConfigurationBuilder().Build());
        this.services = collection.BuildServiceProvider();
        this.Placements = this.services.GetRequiredService<IItemPlacementDefinitionSource>();
        this.EquipmentSlots = this.services.GetRequiredService<IEquipmentSlotDefinitionSource>();
    }

    public IItemPlacementDefinitionSource Placements { get; }

    public IEquipmentSlotDefinitionSource EquipmentSlots { get; }

    public void Dispose() => this.services.Dispose();

}

internal sealed class RefusedEquipIsRecordedWithItsTemplateTestsWearEligibility : Dfo.Server.Abstractions.Inventory.IEquipmentWearDomainService
{
    public ValueTask<bool> CanWearAsync(Dfo.Server.Data.Characters.CharacterId characterId, int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
