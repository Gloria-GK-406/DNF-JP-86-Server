using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class SelectCharacterResponseComposerTests
{
    private static readonly (byte Command, ushort Type, int Occurrence)[] ExpectedStaticIdentities =
    [
        (1, 0x0004, 0), (0, 0x000D, 0), (0, 0x000D, 0), (0, 0x000D, 0),
        (0, 0x000D, 0), (0, 0x000D, 0), (0, 0x0069, 0), (0, 0x0002, 0),
        (0, 0x0002, 1), (0, 0x0245, 0), (0, 0x0013, 0), (0, 0x0015, 0),
        (0, 0x0164, 0), (0, 0x0286, 0), (0, 0x00AD, 0), (0, 0x01C7, 0),
        (0, 0x006C, 0), (0, 0x0005, 0), (0, 0x0187, 0), (0, 0x01B9, 0),
        (0, 0x0166, 0), (0, 0x0166, 1), (0, 0x0166, 2), (0, 0x0166, 3),
        (0, 0x0166, 4), (0, 0x0167, 0), (0, 0x015F, 0), (0, 0x00AC, 0),
        (0, 0x00AE, 0), (0, 0x02DA, 0), (0, 0x017B, 0), (0, 0x0381, 0),
        (0, 0x0381, 1), (0, 0x021F, 0), (0, 0x0357, 0), (0, 0x00FB, 0),
        (0, 0x00CD, 0), (0, 0x019F, 0), (0, 0x00B1, 0), (0, 0x0300, 0),
        (1, 0x0312, 0), (0, 0x03D8, 0), (0, 0x019D, 0), (0, 0x025B, 0),
        (0, 0x0331, 0), (0, 0x01EB, 0), (0, 0x0061, 0), (0, 0x0158, 0),
        (0, 0x02D5, 0), (0, 0x02D5, 1), (0, 0x02D5, 2), (0, 0x01A8, 0),
        (0, 0x007C, 0), (0, 0x0002, 2), (0, 0x0035, 0), (0, 0x0111, 0),
        (0, 0x0016, 0), (0, 0x0077, 0),
    ];

    [Fact]
    public void SelectCharacterStaticSequence_HasExactReviewedMechanicalIdentity()
    {
        Assert.Equal(58, SelectCharacterStaticSequence.Descriptors.Count);
        Assert.Equal(
            ExpectedStaticIdentities,
            SelectCharacterStaticSequence.Descriptors.Select(value =>
                (value.Command, value.Type, value.Occurrence)).ToArray());
        Assert.Equal(Enumerable.Range(1, 58),
            SelectCharacterStaticSequence.Descriptors.Select(value => value.Row));
        Assert.Equal(
            new InventoryContainerType?[]
            {
                InventoryContainerType.Main, InventoryContainerType.Avatar,
                InventoryContainerType.PersonalCargo, InventoryContainerType.Pet,
                InventoryContainerType.AccountCargo,
            },
            SelectCharacterStaticSequence.Descriptors.Skip(1).Take(5)
                .Select(value => value.InventoryContainer).ToArray());
    }

    [Fact]
    public void SelectCharacterResponseComposer_ConditionalRowsSkipOnlyThemselvesAndRow38IsOnce()
    {
        var frames = SelectCharacterResponseComposer.Encode(SelectCharacterPayloadTestData.CreateComplete());
        var identities = Identities(frames);
        var expected = ExpectedStaticIdentities
            .Where((_, index) => index + 1 != 10)
            .Select(value => (value.Command, value.Type)).ToArray();

        Assert.Equal(expected, identities);
        Assert.Equal(1, identities.Count(value => value == ((byte)0, (ushort)0x019F)));
        var subtype1Index = Array.IndexOf(identities, ((byte)0, (ushort)0x0002), 8);
        Assert.Equal(((byte)0, (ushort)0x0013), identities[subtype1Index + 1]);
        Assert.Equal(((byte)0, (ushort)0x0077), identities[^1]);
    }

    [Fact]
    public void SelectCharacterResponseComposer_DarkKnightX01OccursOnceImmediatelyAfterRow11()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var result = source with
        {
            Character = source.Character with
            {
                Identity = source.Character.Identity with { Job = 9 },
            },
        };

        var identities = Identities(SelectCharacterResponseComposer.Encode(result));
        var skillIndex = Array.IndexOf(identities, ((byte)0, (ushort)0x0013));

        Assert.Equal(((byte)0, (ushort)0x01C0), identities[skillIndex + 1]);
        Assert.Equal(1, identities.Count(value => value == ((byte)0, (ushort)0x01C0)));
        Assert.Equal(((byte)0, (ushort)0x0015), identities[skillIndex + 2]);
    }

    [Fact]
    public void SelectCharacterResponseComposer_KnightAndResolvedPetWelcomeEmitAllStaticRows()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var result = source with
        {
            Character = source.Character with
            {
                Identity = source.Character.Identity with { Job = 12 },
            },
        };

        var identities = Identities(SelectCharacterResponseComposer.Encode(result));

        Assert.Equal(ExpectedStaticIdentities.Select(value => (value.Command, value.Type)), identities);
        Assert.DoesNotContain(((byte)0, (ushort)0x01C0), identities);
    }

    [Fact]
    public void SelectCharacterResponseComposer_InapplicablePetWelcomeOmitsOnlyRow58()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var nullWelcome = source with
        {
            World = source.World with
            {
                Content = source.World.Content with { PetWelcome = null },
            },
        };
        var emptyWelcome = source with
        {
            World = source.World with
            {
                Content = source.World.Content with
                {
                    PetWelcome = source.World.Content.PetWelcome! with { Lines = [] },
                },
            },
        };
        var expected = ExpectedStaticIdentities
            .Where((_, index) => index + 1 is not (10 or 58))
            .Select(value => (value.Command, value.Type)).ToArray();

        Assert.Equal(expected, Identities(SelectCharacterResponseComposer.Encode(nullWelcome)));
        Assert.Equal(expected, Identities(SelectCharacterResponseComposer.Encode(emptyWelcome)));
    }

    private static (byte Command, ushort Type)[] Identities(IReadOnlyList<byte[]> frames) =>
        frames.Select(frame =>
        {
            Assert.Equal(frame.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3, 4)));
            return (frame[0], BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        }).ToArray();
}
