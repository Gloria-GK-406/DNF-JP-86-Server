using System.Buffers.Binary;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>
/// The bytes the world-entry path puts on the wire for NOTI <c>0x000E</c>, after the frame was
/// lifted out of this slice.
/// </summary>
/// <remarks>
/// <c>InventoryDeltaMessage</c> used to be a state-synchronization type whose only sender was the
/// daily refill at world entry. A second protocol -- a cleared run's card reward -- now sends it,
/// and a slice may not reach into another, so it was moved up to the endpoint layer along with the
/// item-entry writer it shares with the entry snapshot.
///
/// <b>That move was a move and not a rewrite, and this is what says so.</b> The frame's hash below
/// is the one the suite recorded before the move, transcribed rather than re-recorded; the
/// remaining tests pin the properties a hash alone cannot -- that the entry region still agrees
/// with the snapshot's, on two items that differ, and that the body still closes at three plus a
/// hundred and one per entry for more than one count.
/// </remarks>
public sealed class InventoryDeltaLiftTests
{
    /// <summary>
    /// The whole frame the entry path builds for this fixture's one main-container item.
    /// </summary>
    /// <remarks>
    /// Transcribed from <c>SelectCharacterAcceptedTranscriptTests</c>, which has carried this exact
    /// value since before the lift. It covers the fifteen-byte header and the body alike, so any
    /// byte the move disturbed shows up here.
    /// </remarks>
    private const string PreLiftEntryDeltaFrameSha256 =
        "F9BE81C62166A8D13FDD70545AAC31351973CFCF6A84A0421BBFAF9E0E8A27FC";

    /// <summary>One item entry, in bytes. Transcribed rather than read off the constant.</summary>
    private const int ObservedEntryLength = 101;

    /// <summary>The delta's own header: a container type and a count.</summary>
    private const int ObservedDeltaHeaderLength = 3;

    /// <summary>The main container's snapshot header: a type, the shape parameter and a count.</summary>
    private const int ObservedSnapshotHeaderLength = 5;

    /// <summary>How many virtual slots the main snapshot writes before its first real item.</summary>
    private const int LeadingVirtualSlots = 3;

    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The frame the entry path builds is the one it built before the lift, byte for byte.
    /// </summary>
    /// <remarks>
    /// Encoded through the production graph's own registration rather than by calling an encoder
    /// directly, so that a lift which moved the type but lost its wiring fails here too.
    /// </remarks>
    [Fact]
    public void The_entry_paths_delta_frame_is_unchanged_by_the_lift()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var main = source.CharacterInventory.Containers.Single(
            value => value.ContainerType == InventoryContainerType.Main);

        var frame = Assert.Single(EncodeThroughRegisteredEncoders(
            new InventoryDeltaMessage(
                InventoryContainerType.Main, [main.Items[0]], source.World.PreparedAt)));

        Assert.Equal(PreLiftEntryDeltaFrameSha256, Convert.ToHexString(SHA256.HashData(frame)));
    }

    /// <summary>
    /// The delta still writes an item exactly as the snapshot does.
    /// </summary>
    /// <remarks>
    /// The two frames share one writer, and after the lift that writer is the endpoint layer's. Two
    /// items rather than one, differing in slot, identifier, count and durability, so a writer that
    /// had been replaced by a constant blob -- which one sample cannot tell from a correct one --
    /// fails on the second.
    /// </remarks>
    [Fact]
    public void The_delta_writes_an_item_exactly_as_the_snapshot_does()
    {
        var items = TwoItems();
        var snapshot = new InventorySnapshotMessage(
            new InventoryContainerState(InventoryContainerType.Main, 9, items),
            [], [], 0, 0, PreparedAt).EncodeBody();
        var delta = new InventoryDeltaMessage(InventoryContainerType.Main, items, PreparedAt)
            .EncodeBody();

        for (var index = 0; index < items.Count; index++)
        {
            Assert.Equal(
                snapshot.AsSpan(
                    ObservedSnapshotHeaderLength
                        + ((LeadingVirtualSlots + index) * ObservedEntryLength),
                    ObservedEntryLength).ToArray(),
                delta.AsSpan(
                    ObservedDeltaHeaderLength + (index * ObservedEntryLength),
                    ObservedEntryLength).ToArray());
        }

        // And the two entries are not the same bytes as each other, which is what makes the
        // comparison above evidence of a writer rather than of a repeated constant.
        Assert.NotEqual(
            delta.AsSpan(ObservedDeltaHeaderLength, ObservedEntryLength).ToArray(),
            delta.AsSpan(
                ObservedDeltaHeaderLength + ObservedEntryLength, ObservedEntryLength).ToArray());
    }

    /// <summary>
    /// The body closes: a container type, a count, and that many entries of a hundred and one.
    /// </summary>
    /// <remarks>
    /// Three counts, including the empty one. A body whose length was fixed, or whose count field
    /// was written from something other than the list, passes at one of these at most.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void The_delta_body_closes_at_three_plus_a_hundred_and_one_per_entry(int count)
    {
        var body = new InventoryDeltaMessage(
            InventoryContainerType.Main, [.. TwoItems().Take(count)], PreparedAt).EncodeBody();

        Assert.Equal(ObservedDeltaHeaderLength + (count * ObservedEntryLength), body.Length);
        Assert.Equal((byte)InventoryContainerType.Main, body[0]);
        Assert.Equal((ushort)count, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(1)));
    }

    /// <summary>
    /// The container the delta names is the one the change happened in, not a fixed one.
    /// </summary>
    /// <remarks>
    /// The entry path sends one of these per container the refill touched, so the byte at offset
    /// zero has to follow the change rather than say "main" whatever happened.
    /// </remarks>
    [Theory]
    [InlineData(InventoryContainerType.Main)]
    [InlineData(InventoryContainerType.PersonalCargo)]
    public void The_delta_names_the_container_the_change_happened_in(
        InventoryContainerType containerType)
    {
        var body = new InventoryDeltaMessage(containerType, TwoItems(), PreparedAt).EncodeBody();

        Assert.Equal((byte)containerType, body[0]);
    }

    /// <summary>
    /// One committed change becomes one frame, and a batch of them keeps its order.
    /// </summary>
    /// <remarks>
    /// This is the shared road itself: world entry hands its committed updates to the same function
    /// a card flip hands its one to, so the two cannot drift apart into two ways of saying the same
    /// thing.
    /// </remarks>
    [Fact]
    public void A_batch_of_committed_changes_becomes_one_frame_each_in_order()
    {
        var items = TwoItems();
        var updates = new[]
        {
            new CommittedInventoryUpdateState(InventoryContainerType.Main, [items[0]], PreparedAt),
            new CommittedInventoryUpdateState(
                InventoryContainerType.PersonalCargo, [items[1]], PreparedAt),
        };

        var told = GameInventoryDelivery.Notifications(updates);

        Assert.Equal(
            updates.Select(update => new InventoryDeltaMessage(
                update.ContainerType, update.Items, update.PreparedAt).EncodeBody()),
            told.Select(message => Assert.IsType<InventoryDeltaMessage>(message).EncodeBody()));
    }

    /// <summary>
    /// Two items that differ everywhere the entry writer reads them.
    /// </summary>
    /// <remarks>
    /// Slot, identifier, count and durability all differ, so no comparison over these can pass by
    /// two entries happening to be identical.
    /// </remarks>
    private static IReadOnlyList<InventoryItemState> TwoItems() =>
    [
        new(new ItemIdentity(1), 3, Core(2, 1001, 3, durability: 50)),
        new(new ItemIdentity(2), 17, Core(2, 8475, 99, durability: 12)),
    ];

    private static InventoryItemCoreState Core(
        byte kind, int itemId, int value, ushort durability) => new(
        kind, itemId, value, 4, durability, 1, 6, 7, 8, 9, -1,
        new ChronicleOptionState(0, 0, 0, 0, 0),
        new ChronicleOptionState(0, 0, 0, 0, 0),
        0, 0, 0, 0, 0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0, 0xFF, 0, new RandomOptionState(0, 0, 0),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static IReadOnlyList<byte[]> EncodeThroughRegisteredEncoders(IGameServerMessage message)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(
            services
                .Where(value => value.ServiceType == typeof(ServerMessageEncoderRegistration))
                .Select(value => Assert.IsType<ServerMessageEncoderRegistration>(
                    value.ImplementationInstance)),
            value => value.MessageType == message.GetType());
        var encoder = Assert.IsAssignableFrom<IServerMessageEncoder>(
            provider.GetRequiredService(registration.EncoderType));
        return encoder.Encode(message);
    }
}
