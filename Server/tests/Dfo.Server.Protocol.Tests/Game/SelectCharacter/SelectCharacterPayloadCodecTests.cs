using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Tests.Support;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

public sealed class SelectCharacterPayloadCodecTests
{
    [Fact]
    public void Black_diamond_uses_ordinary_entry_list_and_does_not_occupy_devil_slots()
    {
        var result = SelectCharacterPayloadTestData.Create();
        var grant = new AccountPremiumEntryState(56, 1731536000L);
        var changed = result with { Account = result.Account with
        {
            Entitlements = new AccountEntitlementEntryState([grant]),
        }};
        var body = EncodeAcknowledgement(changed);
        Assert.Equal(1, body[17]);
        Assert.Equal(56, body[18]);
        Assert.Equal(31536000L, BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(19, 8)));
        Assert.Equal(1234, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(27, 4)));
        Assert.Equal(new PremiumServiceStateMessage(new([]), [], DateTimeOffset.FromUnixTimeSeconds(1700000000)).EncodeBody(),
            new PremiumServiceStateMessage(new([grant]), [], DateTimeOffset.FromUnixTimeSeconds(1700000000)).EncodeBody());
    }

    [Fact]
    public void Premium_snapshot_preserves_all_eight_unsigned_expiries_and_single_byte_counts()
    {
        var now=DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var entries=Enumerable.Range(0,8).Select(slot=>new AccountPremiumEntryState(580+slot,2200000000L+slot)).ToArray();
        var usage=Enumerable.Range(0,8).Select(slot=>new CharacterPremiumUsageSlotState((byte)slot,slot==7?255:slot)).ToArray();
        var body=new PremiumServiceStateMessage(new(entries),usage,now).EncodeBody();
        Assert.Equal(77,body.Length);
        Assert.Equal(new byte[]{1,1,0},body[..3]);
        for(var slot=0;slot<8;slot++)
        {
            Assert.Equal(2200000000U+(uint)slot,BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(9+slot*9,4)));
            Assert.Equal((byte)(slot==7?255:slot),body[13+slot*9]);
            if(slot<7) Assert.Equal(new byte[4],body.AsSpan(14+slot*9,4).ToArray());
        }
    }

    [Fact]
    public void Premium_snapshot_clears_expired_missing_slots_and_rejects_invalid_counts()
    {
        var now=DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var entitlements=new AccountEntitlementEntryState([new(585,1700000000),new(586,1699999999)]);
        var body=new PremiumServiceStateMessage(entitlements,[new(5,10),new(6,20)],now).EncodeBody();
        Assert.Equal(new byte[74],body[3..]);
        Assert.Equal(body,new PremiumServiceStateMessage(new([]),[],now).EncodeBody());
        Assert.Throws<InvalidDataException>(()=>new PremiumServiceStateMessage(new([]),[new(7,256)],now).EncodeBody());
    }

    [Fact]
    public void Internal_service_records_are_not_truncated_to_ordinary_contract_types()
    {
        var result = SelectCharacterPayloadTestData.Create();
        var changed = result with { Account = result.Account with
        {
            Entitlements = new AccountEntitlementEntryState([.. result.Account.Entitlements.Premiums,
                .. Enumerable.Range(580,8).Select(type => new AccountPremiumEntryState(type,1700000600))]),
        }};
        Assert.Equal(EncodeAcknowledgement(result),EncodeAcknowledgement(changed));
    }

    [Theory]
    [InlineData(1699999999L, 27)]
    [InlineData(1700000000L, 27)]
    [InlineData(1699999999L, 97)]
    [InlineData(1700000000L, 97)]
    public void Entry_omits_expired_premiums_without_shifting_wallet_or_quest_fields(long expiry, int type)
    {
        var result = SelectCharacterPayloadTestData.Create();
        var expected = EncodeAcknowledgement(result);
        var changed = result with
        {
            Account = result.Account with
            {
                Entitlements = new AccountEntitlementEntryState(
                    [.. result.Account.Entitlements.Premiums, new AccountPremiumEntryState(type, expiry)]),
            },
        };
        Assert.Equal(expected, EncodeAcknowledgement(changed));
        Assert.Equal(2, changed.Account.Entitlements.Premiums.Count);
    }

    [Fact]
    public void SelectCharacterPayload_AcknowledgementProjectsPremiumWalletAndQuestSlots()
    {
        var result = SelectCharacterPayloadTestData.Create();

        var body = EncodeAcknowledgement(result);

        Assert.Equal(271, body.Length);
        Assert.Equal(1, body[0]);
        Assert.Equal(1_700_000_000, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5, 4)));
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(9, 2)));
        Assert.Equal(1, body[17]);
        Assert.Equal(7, body[18]);
        Assert.Equal(600L, BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(19, 8)));
        Assert.Equal(1234, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(27, 4)));
        Assert.Equal(55, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(31, 2)));
        Assert.Equal(66u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(33, 4)));
        Assert.Equal(77, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(211, 4)));
    }

    /// <summary>
    /// The byte the reference server calls <c>AckCharSlotIndex</c> carries the entry town, not the
    /// roster slot. The client resolves it against a keyed town manager and dereferences the result
    /// without a null check, so a slot that is not also a declared town id terminates it -- which is
    /// why a character in slot 0 crashed on entry while slots 1, 2 and 5 did not.
    /// </summary>
    [Fact]
    public void SelectCharacterPayload_AcknowledgementCarriesTheEntryTownRatherThanTheSlot()
    {
        var result = SelectCharacterPayloadTestData.Create();
        Assert.NotEqual(AcknowledgementTownId, result.Character.Identity.SlotIndex);

        var body = EncodeAcknowledgement(result);

        // Offset 218 of the live 261-byte body; this fixture's premium entry and non-tutorial level
        // shift the tail by nine bytes.
        Assert.Equal(AcknowledgementTownId, body[227]);
    }

    [Fact]
    public void SelectCharacterPayload_AcknowledgementRefusesATownTheClientCannotResolve()
    {
        var result = SelectCharacterPayloadTestData.Create();

        var error = Assert.Throws<InvalidDataException>(
            () => new CharacterEntryAcceptedMessage(
                result.Character,
                result.Account,
                result.CharacterEntry.Quests,
                0,
                result.World.PreparedAt).EncodeBody());

        Assert.Contains("town", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes()
    {
        var result = SelectCharacterPayloadTestData.Create();

        var main = EncodeInventory(
            result, InventoryContainerType.Main);
        var avatar = EncodeInventory(
            result, InventoryContainerType.Avatar);

        // Two A21 measurements move every number here, and neither changes what is asserted: the
        // common entry is 101 bytes rather than 84, and the main container carries fourteen virtual
        // slots rather than nine. The item id is still read back from the same field at the offset
        // those two facts put it at.
        Assert.Equal(5 + (15 * 101), main.Length);
        Assert.Equal((byte)InventoryContainerType.Main, main[0]);
        Assert.Equal(15, BinaryPrimitives.ReadUInt16LittleEndian(main.AsSpan(3, 2)));
        Assert.Equal(1001, BinaryPrimitives.ReadInt32LittleEndian(main.AsSpan(310, 4)));

        // Correction to a correction (2026-09-07). The line above this block briefly asserted
        // 5 + 101 and read nothing out of the entry. That came from removing the trailing block
        // outright, which was the right fix for the main bag and the wrong one here: the client
        // picks its reader by container, and for the avatar list it reads a length-prefixed block
        // after every record. Sending nothing there left it reading the receive buffer's padding as
        // a length. The block, and these four values with it, are back -- and the frame's SHA-256
        // is not the one it had before: what was there was the older client A12 shape, thirty bytes of
        // jewel socket where A21 sends eighteen. Restored is the block, at the A21 length.
        Assert.Equal(5 + 101 + 30, avatar.Length);
        Assert.Equal((byte)InventoryContainerType.Avatar, avatar[0]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(avatar.AsSpan(3, 2)));
        Assert.Equal(18, BinaryPrimitives.ReadInt32LittleEndian(avatar.AsSpan(106, 4)));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(avatar.AsSpan(128, 4)));
        Assert.Equal(0x1122, BinaryPrimitives.ReadUInt16LittleEndian(avatar.AsSpan(132, 2)));
        Assert.Equal(0x3344, BinaryPrimitives.ReadUInt16LittleEndian(avatar.AsSpan(134, 2)));
    }

    [Theory]
    [InlineData(0x006C, 3)]
    [InlineData(0x017B, 11)]
    [InlineData(0x021F, 4)]
    // A21 gives the completion marker an eight-byte body; A12's was empty. The frame is still a
    // pure signal -- both observed bodies are constants -- but "zero length" is no longer what
    // identifies it.
    [InlineData(0x007C, 8)]
    [InlineData(0x0111, 8)]
    public void SelectCharacterPayload_ObservedConstantBodiesHaveExactLengths(
        ushort type,
        int length)
    {
        Assert.Equal(length, EncodeObservedConstant(type).Length);
    }

    [Fact]
    public void SelectCharacterPayload_LotteryPreservesPopulatedAndLoadedEmptyAccountState()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        source = source with
        {
            Account = source.Account with
            {
                Progression = source.Account.Progression with
                {
                    LotteryProgress =
                    [
                        new AccountLotteryProgressEntryState(7001, [4, 1, 4]),
                        new AccountLotteryProgressEntryState(7002, [0]),
                    ],
                },
            },
        };

        var populated = EncodeSelectedStateBody(source, 0x03D8);
        var empty = EncodeSelectedStateBody(
            source with
            {
                Account = source.Account with
                {
                    Progression = source.Account.Progression with { LotteryProgress = [] },
                },
            },
            0x03D8);

        Assert.Equal(204, populated.Length);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(populated));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(4)));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(8)));
        Assert.Equal(7001, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(12)));
        Assert.Equal([2, 5, 0], populated.AsSpan(16, 3).ToArray());
        Assert.Equal(7002, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(36)));
        Assert.Equal(1, populated[40]);
        Assert.Equal(new byte[204], empty);
    }

    [Fact]
    public void SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var supportId = new CharacterId(909);
        var ordinary = CoreForTest(1, 111, 22) with
        {
            ChronicleOption0 = new ChronicleOptionState(123, 2, 3, 4, 5),
            ChronicleOption1 = new ChronicleOptionState(0, 91, 92, 93, 94),
            EmblemSocketCount = 1,
            EmblemId1 = 456,
            Rune = 0x7788,
            RandomOption0 = new RandomOptionState(7, 8, 9),
            RandomOption1 = new RandomOptionState(0, 81, 82),
            RandomOptionState = 10,
            RandomOptionChangedIndex = byte.MaxValue,
        };
        var avatar = CoreForTest(8, 222, 9001);
        var creature = CoreForTest(5, 444, 77) with { Marker16 = 555 };
        var support = source.Character with
        {
            Identity = source.Character.Identity with
            {
                CharacterId = supportId,
                Name = [0x53],
            },
        };
        var supportEntry = source.CharacterEntry with
        {
            CharacterId = supportId,
            Public = source.CharacterEntry.Public with
            {
                Extras = source.CharacterEntry.Public.Extras with { SkillTreeIndex = 0 },
            },
        };
        var supportInventory = source.CharacterInventory with
        {
            CharacterId = supportId,
            Containers =
            [
                new InventoryContainerState(InventoryContainerType.Equipment, 0,
                [
                    new InventoryItemState(new ItemIdentity(11), 1, ordinary),
                    new InventoryItemState(new ItemIdentity(12), 2, avatar,
                        new InventoryAvatarDetailState(
                            checked((int)source.World.PreparedAt.AddSeconds(300).ToUnixTimeSeconds()),
                            333, Enumerable.Range(0, 30).Select(value => (byte)value).ToArray(),
                            0x1122, 0x3344, 0)),
                    new InventoryItemState(new ItemIdentity(13), 25, creature, Noti2CreatureMarker: 555),
                ]),
            ],
        };
        var relation = new AccountCharacterRelationEntryState(
            source.Character.Identity.CharacterId, 0, supportId, 77, 88,
            new AccountSupportCharacterEntryState(support, supportEntry, supportInventory));
        source = source with
        {
            Account = source.Account with
            {
                Relations = source.Account.Relations with { CharacterRelations = [relation] },
            },
        };

        var populated = EncodeSelectedStateBody(source, 0x019F);
        var empty = EncodeSelectedStateBody(
            source with
            {
                Account = source.Account with
                {
                    Relations = source.Account.Relations with { CharacterRelations = [] },
                },
            },
            0x019F);

        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(populated));
        Assert.Equal(source.Character.Identity.CharacterId.Value,
            BinaryPrimitives.ReadUInt16LittleEndian(populated.AsSpan(2)));
        // Three entries, each ending in the A21 tail the older client had no room for: nine
        // zero bytes for a costume-bar or creature slot, seventeen for an ordinary one. All
        // three of these are costume-bar or creature slots, so the body grew by 27.
        //
        // It then lost twelve of those again when the jewel run went to the eighteen bytes A21
        // takes, from the thirty that are stored. That is the only reason this figure and every
        // offset after the avatar's jewel run moved; the entry is otherwise unchanged, and the
        // offsets before the run are the same numbers they were.
        Assert.Equal(335, populated.Length);
        Assert.Equal(3, populated[100]);

        // Ordinary slot: NOTI-2 begins with u8 slot and uses dynamic sections.
        Assert.Equal(1, populated[101]);
        Assert.Equal(111, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(102)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(populated.AsSpan(113)));
        Assert.Equal(1, populated[137]);
        Assert.Equal(123, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(138)));
        Assert.Equal(1, populated[150]);
        Assert.Equal(456, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(151)));
        Assert.Equal(1, populated[157]);
        Assert.Equal([7, 8, 9, 10, 0xFF], populated.AsSpan(158, 5).ToArray());

        // Avatar slot: remaining seconds, clear-avatar u32, jewel socket and colors.
        Assert.Equal(2, populated[182]);
        Assert.Equal(222, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(183)));
        Assert.Equal(300u, BinaryPrimitives.ReadUInt32LittleEndian(populated.AsSpan(187)));
        Assert.Equal(333u, BinaryPrimitives.ReadUInt32LittleEndian(populated.AsSpan(194)));
        Assert.Equal(18, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(206)));
        Assert.Equal(Enumerable.Range(0, 18).Select(value => (byte)value),
            populated.AsSpan(210, 18).ToArray());
        Assert.Equal(0x1122, BinaryPrimitives.ReadUInt16LittleEndian(populated.AsSpan(232)));
        Assert.Equal(0x3344, BinaryPrimitives.ReadUInt16LittleEndian(populated.AsSpan(234)));

        // Candidate creature slot 25 carries the NOTI-2 creature marker after the enchant block.
        Assert.Equal(25, populated[264]);
        Assert.Equal(444, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(265)));
        Assert.Equal(555u, BinaryPrimitives.ReadUInt32LittleEndian(populated.AsSpan(288)));
        Assert.Equal([0, 0], empty);
        Assert.Throws<InvalidDataException>(() => EncodeSelectedStateBody(
            source with
            {
                Account = source.Account with
                {
                    Relations = source.Account.Relations with
                    {
                        CharacterRelations = [relation with { SupportProjection = null }],
                    },
                },
            },
            0x019F));
        Assert.Throws<InvalidDataException>(() => EncodeSelectedStateBody(
            source with
            {
                Account = source.Account with
                {
                    Relations = source.Account.Relations with
                    {
                        CharacterRelations = [relation with
                        {
                            SupportProjection = relation.SupportProjection! with
                            {
                                Entry = supportEntry with { CharacterId = new CharacterId(910) },
                            },
                        }],
                    },
                },
            }, 0x019F));
        Assert.Throws<InvalidDataException>(() => EncodeSelectedStateBody(
            source with
            {
                Account = source.Account with
                {
                    Relations = source.Account.Relations with
                    {
                        CharacterRelations = [relation with
                        {
                            SupportProjection = relation.SupportProjection! with
                            {
                                Inventory = supportInventory with { CharacterId = new CharacterId(911) },
                            },
                        }],
                    },
                },
            }, 0x019F));
        Assert.Throws<InvalidDataException>(() => EncodeSelectedStateBody(
            source with
            {
                Account = source.Account with
                {
                    Relations = source.Account.Relations with
                    {
                        CharacterRelations = [relation with
                        {
                            OwnerCharacterId = new CharacterId(999),
                        }],
                    },
                },
            }, 0x019F));
    }

    [Fact]
    public void SelectCharacterPayload_AllStaticBodyFamiliesEncodePopulatedTypedValues()
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();
        var cases = new (ushort Type, int Occurrence)[]
        {
            (0x0002, 0), (0x0002, 1), (0x0002, 2), (0x0005, 0),
            (0x0013, 0), (0x0015, 0), (0x0016, 0), (0x0035, 0),
            (0x0061, 0), (0x0069, 0), (0x006C, 0), (0x00AC, 0),
            (0x00AD, 0), (0x00AE, 0), (0x00B1, 0), (0x00CD, 0),
            (0x00FB, 0), (0x0158, 0), (0x015F, 0), (0x0164, 0),
            (0x0166, 0), (0x0166, 1), (0x0166, 2), (0x0166, 3),
            (0x0166, 4), (0x0167, 0), (0x017B, 0), (0x0187, 0),
            (0x019D, 0), (0x019F, 0), (0x01A8, 0), (0x01B9, 0),
            (0x01C7, 0), (0x01EB, 0), (0x021F, 0), (0x0245, 0),
            (0x025B, 0), (0x0286, 0), (0x02D5, 0), (0x02D5, 1),
            // 0x0344, 0x036A and 0x03C1 are the A21 numbers for what A12 numbered 0x02DA, 0x0300
            // and 0x0357. The other four in this list keep their numbers: A21's own server sends
            // those bodies at 0x02D5, 0x0331, 0x0381 and 0x03D8, whatever it calls them.
            (0x02D5, 2), (0x0344, 0), (0x036A, 0), (0x0331, 0),
            (0x03C1, 0), (0x0381, 0), (0x0381, 1), (0x03D8, 0),
            (0x007C, 0), (0x0111, 0), (0x01C0, 0), (0x0077, 0),
            // The sixth ITEM_LIST: A21's guild-medal container, which the older client did not have.
            (0x000D, 5),
        };

        foreach (var @case in cases)
            Assert.NotNull(EncodeSelectedStateBody(
                result, @case.Type, @case.Occurrence));

        Assert.Equal(77, EncodePremiumService(result).Length);
        // MERCENARY_INFO is a CMD, so like PREMIUM_SERVICE it is built directly rather than looked
        // up in the batch. Seven bytes: the envelope's acceptance and an empty expedition roster.
        Assert.Equal(
            new byte[] { 0x01, 0, 0, 0, 0, 0, 0 },
            new MercenarySupportStateMessage().EncodeBody());
    }

    [Fact]
    public void SelectCharacterPayload_HighRiskAvailabilityDistinctionsAreNotZeroFilled()
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();

        Assert.Equal([9, 8], EncodeSelectedStateBody(result, 0x0187));
        Assert.Throws<InvalidDataException>(() => EncodeSelectedStateBody(
            result with
            {
                CharacterEntry = result.CharacterEntry with
                {
                    Skills = result.CharacterEntry.Skills with { ProtocolMirror = null },
                },
            },
            0x0013));
        Assert.Throws<InvalidDataException>(() => EncodeSelectedStateBody(
            result with
            {
                CharacterEntry = result.CharacterEntry with
                {
                    Features = result.CharacterEntry.Features with
                    {
                        Collections = result.CharacterEntry.Features.Collections with
                        {
                            CollectionBoxes =
                            [result.CharacterEntry.Features.Collections.CollectionBoxes[0] with { Protocol = null }],
                        },
                    },
                },
            },
            0x0381));
        Assert.Throws<InvalidDataException>(() => EncodeInventory(
            result with
            {
                CharacterInventory = result.CharacterInventory with
                {
                    Containers = result.CharacterInventory.Containers.Select(container =>
                        container.ContainerType == InventoryContainerType.Avatar
                            ? container with
                            {
                                Items = container.Items.Select(item => item with { AvatarDetail = null }).ToArray(),
                            }
                            : container).ToArray(),
                },
            },
            InventoryContainerType.Avatar));

        // An unavailable PvP result list used to have to throw here, because subtype 1 wrote the
        // list and a null would have gone out as a count of zero -- "no results" where the truth is
        // "not loaded". A21's subtype 1 does not carry PvP results at all, so there is no longer a
        // wire value that could be mistaken for one, and nothing for this frame to refuse.
        // Whether the domain should still be loading them is a separate question, recorded in
        // Docs/ProtocolScaffoldDissonances.md rather than answered here.
    }

    [Fact]
    public void SelectCharacterPayload_AcknowledgementOmitsLegacyInternalServiceKey()
    {
        var body = EncodeAcknowledgement(
            SelectCharacterPayloadTestData.CreateComplete());

        Assert.Equal(0, body[17]);
    }

    [Fact]
    public void SelectCharacterPayload_Subtype0PetAliveTracksPetRuntimeField04()
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();
        var petCore = CoreForTest(5, 777, 44);
        result = result with
        {
            CharacterInventory = result.CharacterInventory with
            {
                Containers = result.CharacterInventory.Containers.Select(container =>
                    container.ContainerType == InventoryContainerType.Equipment
                        ? container with
                        {
                            Items = [.. container.Items, new InventoryItemState(new ItemIdentity(99), 25, petCore)],
                        }
                        : container).ToArray(),
            },
            CharacterEntry = result.CharacterEntry with
            {
                Features = result.CharacterEntry.Features with
                {
                    Pets = [result.CharacterEntry.Features.Pets[0] with { Field04 = 0 }],
                },
            },
        };

        var body = EncodeSelectedStateBody(result, 0x0002, 0);
        var petName = new byte[] { 1, 0, 0, 0, 0x50 };
        var offset = body.AsSpan().IndexOf(petName);

        Assert.True(offset >= 0);
        Assert.Equal(0, body[offset + petName.Length]);
    }

    [Fact]
    public void SelectCharacterPayload_MailboxSummaryPreservesGoldAndAttachmentIdentity()
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();
        var message = result.CharacterEntry.Features.Mailbox.Messages[0] with
        {
            Gold = 321,
            GoldAttachments = [new MailGoldAttachmentState(2, 321)],
            Attachments =
            [
                new CharacterMailboxAttachmentEntryState(
                    1, 0, 1, 0, 3, 4, 444, "stackable", 5, 5,
                    0, 0, 0, 0, 0, 0, RawCore(6, 444, 5), ""),
            ],
        };
        result = result with
        {
            CharacterEntry = result.CharacterEntry with
            {
                Features = result.CharacterEntry.Features with
                {
                    Mailbox = new CharacterMailboxEntryState(2, [message]),
                },
            },
        };

        var body = EncodeSelectedStateBody(result, 0x0061);

        Assert.Equal(2, body[0]);
        Assert.Equal(0x40000001, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(2, 4)));
        var senderBytes = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(10, 4));
        var firstGold = 14 + senderBytes;
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(firstGold, 4)));
        Assert.Equal(444, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(firstGold + 4, 4)));
        Assert.Equal(1, body[firstGold + 8]);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(firstGold + 42, 4)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(firstGold + 50, 4)));
        Assert.Equal(0, body[firstGold + 62]);
        Assert.Equal(0, body[firstGold + 71]);
        var secondSummary = firstGold + 72;
        Assert.Equal(0x40000002, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(secondSummary, 4)));
        Assert.Equal(321, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(secondSummary + 12 + senderBytes, 4)));
    }

    [Fact]
    public void SelectCharacterPayload_MailboxSummaryIgnoresRandomOptionAuxiliaryBytesWhenTypeIsZero()
    {
        var cleanCore = RawCore(6, 444, 5);
        var staleCore = cleanCore.ToArray();
        staleCore[57] = 0x51;
        staleCore[58] = 0x52;

        static SelectCharacterResult WithAttachment(SelectCharacterResult result, byte[] core)
        {
            var message = result.CharacterEntry.Features.Mailbox.Messages[0] with
            {
                Attachments =
                [
                    new CharacterMailboxAttachmentEntryState(
                        1, 0, 1, 0, 3, 4, 444, "stackable", 5, 5,
                        0, 0, 0, 0, 0, 0, core, ""),
                ],
            };
            return result with
            {
                CharacterEntry = result.CharacterEntry with
                {
                    Features = result.CharacterEntry.Features with
                    {
                        Mailbox = new CharacterMailboxEntryState(2, [message]),
                    },
                },
            };
        }

        var clean = EncodeSelectedStateBody(
            WithAttachment(SelectCharacterPayloadTestData.CreateComplete(), cleanCore), 0x0061);
        var stale = EncodeSelectedStateBody(
            WithAttachment(SelectCharacterPayloadTestData.CreateComplete(), staleCore), 0x0061);

        Assert.Equal(clean, stale);
    }

    [Fact]
    public void SelectCharacterPayload_Subtype1EndsWithTheA21DimensionRollAndItsConstantRuns()
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();

        var body = EncodeSelectedStateBody(result, 0x0002, 1);

        // A21 ends subtype 1 with a fixed roll of twenty-six dimension keys and two constant runs
        // around the manage level and manage point. What stood here before was the older client's
        // tail -- four dimension flags, a PvP result list, a special-reward quest list and six stat
        // fields -- and none of it is on A21's wire.
        //
        // The values below are not a re-recording of whatever the encoder now produces: every byte
        // is read off the reference session's own subtype 1, whose last 187 bytes this reproduces
        // exactly. See patterns/data/traces/a21-entry-batch-diff-20260825.md.
        var tail = body[^187..];
        Assert.Equal(26, tail[0]);
        Assert.Equal(11006u, BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(1, 4)));
        Assert.Equal(3, tail[6]);
        Assert.Equal(4123u, BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(1 + (25 * 6), 4)));
        Assert.Equal(3, tail[1 + (25 * 6) + 5]);
        Assert.Equal(
            new byte[] { 0x02, 0x00, 0x05, 0x00, 0x6F, 0x00, 0x00, 0x00, 0x00 },
            tail[157..166]);
        Assert.Equal(8, tail[166]);                                  // manage level, from the domain
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(167, 4)));
        Assert.Equal(
            new byte[] { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            tail[171..187]);
    }

    [Fact]
    public async Task SelectCharacterPayload_Subtype1PreservesRealSqliteSpecialRewardSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-row9-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CharacterSide(path).EntryState(new GoldLimitRuleService());
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES(22,12,'row9',1,60);
                    INSERT INTO character_condition(character_id,pc_room_state) VALUES(22,0);
                    INSERT INTO character_stats(character_id) VALUES(22);
                    INSERT INTO character_pvp(character_id) VALUES(22);
                    INSERT INTO character_settings(character_id) VALUES(22);
                    INSERT INTO character_expert_job(character_id) VALUES(22);
                    UPDATE characters SET manage_level=8, skill_tree_index=0 WHERE character_id=22;
                    INSERT INTO character_invisible_flags(character_id,slot_index,flag_value)
                    VALUES(22,13502,1);
                    -- Two collect-box rows because the entry batch sends two box frames. The
                    -- boxes a character has now come from its stored progress rather than from a
                    -- hard-coded pair, so a fixture that omits them describes a character the
                    -- factory cannot encode.
                    INSERT INTO character_collectbox_slots(character_id,box_index,slot_index,item_id)
                    VALUES(22,1,0,10007322),(22,2,0,10007322);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var services = new ServiceCollection();
            services.AddSingleton(repository);
            services.AddSingleton<Dfo.Server.Abstractions.Accounts.IPremiumServiceDomainService>(new Dfo.Server.Protocol.Tests.Support.CardPremiums());
            services.AddSingleton<Dfo.Server.Abstractions.Notices.INoticeStateDomainService>(new Dfo.Server.Application.Tests.Support.NoticeStateStub());
            services.AddSingleton<ICharacterDomainService>(Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain());
            services.AddSingleton<IQuestDefinitionSource>(new Row9QuestDefinitions(13502));
            // The quest rules read both declarations; nothing on this path asks what a quest pays.
            services.AddSingleton<IQuestParameterSource>(new NoQuestParameters());
            new ApplicationModule().ConfigureServices(
                services, new ConfigurationBuilder().Build());
            // This profile/quest fixture does not register dungeon content or run services.
            services.RemoveAll<Dfo.Server.Abstractions.World.IAltarDungeonDomainService>();
            await using var provider = services.BuildServiceProvider();
            var typed = await provider.GetRequiredService<ICharacterEntryStateDomainService>().LoadAsync(
                new AccountId(12), new CharacterId(22), CancellationToken.None);
            var result = SelectCharacterPayloadTestData.CreateComplete() with
            {
                CharacterEntry = typed,
            };

            var body = EncodeSelectedStateBody(result, 0x0002, 1);

            Assert.Equal([13502], typed.Public.Extras.SpecialRewardQuestIds);
            Assert.Empty(typed.Public.Pvp.Results);

            // The wire assertion that used to follow -- the quest id 23 bytes from the end of
            // subtype 1 -- is gone because A21's subtype 1 does not carry the special-reward list.
            // What this test is named for still holds and is still checked: the id comes out of the
            // real SQLite source rather than the fixture. The body is still built so that a change
            // which makes the domain unencodable fails here rather than silently.
            Assert.NotEmpty(body);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static byte[] RawCore(byte kind, int itemId, int value)
    {
        var bytes = new byte[82];
        bytes[0] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5), value);
        bytes[66] = 0xFF;
        return bytes;
    }

    /// <summary>Nothing on this path works out a quest reward.</summary>
    private sealed class NoQuestParameters : IQuestParameterSource
    {
        public QuestParameters Load() =>
            throw new NotSupportedException(
                "No test on this path asks what a quest pays; a value here would be invented.");
    }

    private sealed class Row9QuestDefinitions(int specialQuestId) : IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([]);

        public ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(questId == specialQuestId
                ? new QuestDefinition(
                    questId, "special", null, null, null, null, null, null, [], "", "",
                    null, null, null, null, null, null, null, [], [], [], [], true)
                : null);

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
    }

    /// <summary>The entry town these layout tests encode. Deliberately not the fixture's slot.</summary>
    private const byte AcknowledgementTownId = 1;

    private static byte[] EncodeAcknowledgement(SelectCharacterResult result) =>
        new CharacterEntryAcceptedMessage(
            result.Character,
            result.Account,
            result.CharacterEntry.Quests,
            AcknowledgementTownId,
            result.World.PreparedAt).EncodeBody();

    private static byte[] EncodeInventory(
        SelectCharacterResult result,
        InventoryContainerType containerType)
    {
        var container = containerType == InventoryContainerType.AccountCargo
            ? result.AccountInventory.Cargo.Container
            : result.CharacterInventory.Containers.Single(
                value => value.ContainerType == containerType);
        return new InventorySnapshotMessage(
            container,
            result.CharacterInventory.VirtualResources,
            result.AccountInventory.VirtualResources,
            result.AccountInventory.Cargo.SelectionKey,
            result.AccountInventory.Cargo.Value32,
            result.World.PreparedAt).EncodeBody();
    }

    private static byte[] EncodeObservedConstant(ushort type) => type switch
    {
        0x006C => new ServerEventBaselineMessage().EncodeBody(),
        0x017B => new ClientEffectBaselineMessage().EncodeBody(),
        0x021F => new BossTowerBaselineMessage().EncodeBody(),
        0x007C => new EntryMarkerMessage().EncodeBody(),
        0x0111 => new CrossServerFriendBaselineMessage().EncodeBody(),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static byte[] EncodeSelectedStateBody(
        SelectCharacterResult result,
        ushort type,
        int occurrence = 0)
    {
        GameStateSynchronizationMessage message;
        if (type == 0x0245)
        {
            message = new KnightShieldDeckMessage(
                result.CharacterEntry.Skills.KnightShieldDeck
                ?? throw new InvalidDataException("Knight shield deck is unavailable."));
        }
        else if (type == 0x01C0)
        {
            message = new DarkKnightComboStateMessage(
                result.CharacterEntry.Skills.DarkKnightCombos
                ?? throw new InvalidDataException("Dark Knight combo state is unavailable."));
        }
        // Both left the A21 entry sequence but keep their encoders, so they are built here for the
        // same reason 0x0245 and 0x01C0 are: the body is still worth pinning, and reaching it
        // through the batch would silently stop testing anything the day it is not in the batch.
        else if (type == 0x019F)
        {
            message = new SupportCharacterStateMessage(
                result.Character.Identity.CharacterId,
                result.Account.Relations.CharacterRelations,
                result.World.PreparedAt);
        }
        else if (type == 0x019D)
        {
            message = new LuckyStarStateMessage(result.Account.Wallet.LuckyStar);
        }
        else
        {
            var matches = SelectedCharacterStateMessageFactory.Create(result)
                .Where(value => value is IGameNotiMessage && value.Type == type)
                .ToArray();
            if (occurrence < 0 || occurrence >= matches.Length)
                throw new ArgumentOutOfRangeException(nameof(occurrence));
            message = matches[occurrence];
        }
        return message.EncodeBody();
    }

    private static byte[] EncodePremiumService(SelectCharacterResult result) =>
        new PremiumServiceStateMessage(
            result.Account.Entitlements,
            result.CharacterEntry.Progression.PremiumUsageSlots, result.World.PreparedAt).EncodeBody();

    private static InventoryItemCoreState CoreForTest(byte kind, int itemId, int value) => new(
        kind, itemId, value, 4, 50, 1, 6, 7, 8, 9, -1,
        new ChronicleOptionState(0, 0, 0, 0, 0),
        new ChronicleOptionState(0, 0, 0, 0, 0),
        0, 0, 0, 0, 0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0, 0xFF, 0, new RandomOptionState(0, 0, 0),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

internal static class SelectCharacterPayloadTestData
{
    public static SelectCharacterResult Create()
    {
        var accountId = new AccountId(1);
        var characterId = new CharacterId(0x1234);
        var core = Core(1, 1001, 3);
        var avatarCore = Core(8, 2002, 9001);
        var inventory = new CharacterInventoryEntryState(
            characterId,
            [
                new InventoryContainerState(InventoryContainerType.Main, 9,
                    [new InventoryItemState(new ItemIdentity(1), 3, core)]),
                new InventoryContainerState(InventoryContainerType.Avatar, 0,
                    [new InventoryItemState(new ItemIdentity(2), 3, avatarCore,
                        new InventoryAvatarDetailState(1_700_000_500, 44,
                            Enumerable.Range(0, 30).Select(value => (byte)value).ToArray(),
                            0x1122, 0x3344, 0))]),
            ],
            [],
            new CharacterItemValueEntryState([], []),
            [],
            new CharacterRentalEntryState([]),
            new InventoryGoldLimitState(0, 0));
        var character = new ActiveCharacterState(
            new CharacterIdentityState(characterId, 4, [0x41], 1, 0),
            new CharacterProgressionState(60, 100),
            new CharacterFatigueState(188, 5),
            new CharacterPositionState(1, 2, 3, 4, 5),
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        return new SelectCharacterResult(
            character,
            new CharacterEntryState(
                characterId,
                null!, null!, null!,
                new CharacterQuestEntryState(
                    [new CharacterQuestProgressState(0, 55, 66)], [], [77], new byte[30_000]),
                null!, null!),
            inventory,
            new AccountInventoryEntryState(accountId,
                new AccountCargoEntryState(accountId,
                    new InventoryContainerState(InventoryContainerType.AccountCargo, 0, []),
                    [], 0, 0, 0), []),
            new AccountEntryState(accountId, null!,
                new AccountWalletEntryState(1234, 2, 3, 4),
                new AccountEntitlementEntryState(
                    [new AccountPremiumEntryState(7, 1_700_000_600)]),
                null!, null!),
            new GameWorldEntryState(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), null!, null!),
            null!,
            new AccountCharacterRoster(accountId, 24, []));
    }

    public static SelectCharacterResult CreateComplete()
    {
        var source = Create();
        var accountId = source.Account.AccountId;
        var characterId = source.Character.Identity.CharacterId;
        var equipmentCore = Core(2, 3003, 4);
        var characterInventory = source.CharacterInventory with
        {
            Containers =
            [
                .. source.CharacterInventory.Containers,
                new InventoryContainerState(InventoryContainerType.PersonalCargo, 8, []),
                new InventoryContainerState(InventoryContainerType.Equipment, 0,
                    [new InventoryItemState(new ItemIdentity(3), 1, equipmentCore)]),
                new InventoryContainerState(InventoryContainerType.Pet, 0, []),
            ],
            ItemValues = new CharacterItemValueEntryState(
                [new InventoryItemValueState(0, 20, 30)],
                [new InventoryItemValueState(0, 21, 31)]),
            EquipmentLocks =
            [new InventoryEquipmentLockState(1, InventoryContainerType.Equipment, 1, 2, 3)],
            Rentals = new CharacterRentalEntryState(
                [new CharacterRentalItemState(40, 41, 1_700_000_900)]),
        };
        var derivedStats = new CharacterDerivedStatsStorageRecord(
            100, 90, 80, 70, 60, 50, 40, 30, 20, 120, 11, 12);
        var duelling = new CharacterPvpStandingStorageRecord(
            7, 0, 0, 0,
            [
                new CharacterPvpResultStorageRow(0x11223344, 0x5566, 0x7788),
                new CharacterPvpResultStorageRow(0x01020304, 0x0506, 0x0708),
            ]);
        var extras = new CharacterEntryExtrasState(
            SkillTreeIndex: 0,
            NameTagItemId: 0,
            NameTagExpireTime: 0,
            ManageLevel: 8,
            CharacterExperience: 500,
            ExtraEquipmentSlotState: 0,
            Dimensions: [(0, 1, 2, 3)],
            DimensionFlags: (4, 5, 6, 7),
            SpecialRewardQuestIds: [0x0A0B0C0D, 0x11121314]);
        var collections = new CharacterCollectionsEntryState(
            Enumerable.Range(0, 5).Select(index => new CharacterTitleBookCategoryState(
                (byte)index, 1, 2,
                [new CharacterTitleBookEntryState(3, 4 + index, 5, 6, 7, 8, 9, 10, 11, 12)])).ToArray(),
            [new CharacterAchievementProgressState(50, 1, 2, 3, 4)],
            [
                new CharacterCollectionBoxProgressState(3,
                    [new CharacterCollectionBoxSlotState(0, 60)],
                    new CharacterCollectionBoxProtocolState(70, 1)),
                new CharacterCollectionBoxProgressState(4, [],
                    new CharacterCollectionBoxProtocolState(uint.MaxValue, 0)),
            ]);
        var features = new CharacterFeaturesEntryState(
            [new CharacterPetRuntimeState(44, 1, 1, 2, 3, 4, 5, [0x50], 6)],
            collections,
            new CharacterExpertJobEntryState(1, 100, 2, 0, 0, 300, [80],
                new CharacterExpertProtocolState([4, 5], 6)),
            new CharacterMailboxEntryState(2,
                [new CharacterMailboxMessageEntryState(
                    90, 91, [0x53], 0, 0, [0x54], [0x42], 0, 100, 200, 1, [])]),
            [new CharacterPvpMissionEntryState(92, 93)]);
        var entry = new CharacterEntryState(
            characterId,
            new CharacterPublicEntryState(
                new CharacterConditionStorageRecord(
                    1, 2, 0, 0, 0, 0, 0, 5, 6, CreatureBuffer: []),
                new CharacterSettingsStorageRecord(4),
                duelling,
                new CharacterExpertJobStorageRecord(1, 3),
                derivedStats,
                new CharacterGuildStandingStorageRecord(0, 0),
                extras,
                new CharacterAppearanceCompatibilityState(1, [0]),
                4,
                5,
                new CharacterPublicIdentityEntryState(6, 7, 8)),
            new CharacterSettingsEntryState([1, 2], [9, 8], 4) { SeenNotices = [1, 2] },
            new CharacterSkillsEntryState(
                [new CharacterSkillPageState(0,
                    [new CharacterSkillState(1, 100, 2, [3])]), new CharacterSkillPageState(1, [])],
                [new CharacterSkillPointSlotState(1, 100)],
                new CharacterSkillPointBasisState(0, 0),
                [new DarkKnightComboEntryState(0, [1, 2])],
                [10, 11, 12, 13, 14],
                new CharacterSkillProtocolMirrorState(200, 201, 202, 203)),
            source.CharacterEntry.Quests,
            new CharacterProgressionEntryState(
                [1, 2], 3, 4, 5, 6, 7, 8, 9, 10, true, 60,
                [new CharacterDailyChallengeGroupState(0, 11,
                    [new CharacterDailyChallengeItemState(12, 13, 14)], true)],
                [1, 0, 0, 0, 0, 0], [15],
                [
                    new CharacterScheduleCompatibilityState(0, 1, 2, 3, 4),
                    new CharacterScheduleCompatibilityState(1, 5, 6, 7, 8),
                    new CharacterScheduleCompatibilityState(2, 9, 10, 11, 12),
                ],
                [new CharacterDungeonProgressState(16, 2)],
                [new CharacterPremiumUsageSlotState(1, 3)]),
            features);
        var world = source.World with
        {
            Content = new WorldContentEntryState(
                new AvailableQuestEntryState([new OfferableQuestState(17, "q", 0)]),
                DailyChallengeEntryState.Empty,
                [],
                new PetWelcomeEntryState(18, "pet", ["hello"])),
            Channel = new ChannelEntryPolicyState(1,
                new EntrySpawnState(1, 2, 30, 40, 5, 0, false), true, false),
        };
        var account = source.Account with
        {
            Settings = new AccountSettingsEntryState([1], [2], [3], 4, null),
            Progression = new AccountProgressionEntryState(0, 0,
                new AccountAdventureEntryState([60]), []),
            Relations = new AccountRelationCompatibilityEntryState([], [],
                [new AccountCompatibilityEntryState(20, 21, 22)], 2),
            Entitlements = new AccountEntitlementEntryState(
                [new AccountPremiumEntryState(581, 1_700_001_000)]),
        };
        return source with
        {
            CharacterEntry = entry,
            CharacterInventory = characterInventory,
            Account = account,
            World = world,
        };
    }

    private static InventoryItemCoreState Core(byte kind, int itemId, int value) => new(
        kind, itemId, value, 4, 50, 1, 6, 7, 8, 9, -1,
        new ChronicleOptionState(0, 0, 0, 0, 0),
        new ChronicleOptionState(0, 0, 0, 0, 0),
        0, 0, 0, 0, 0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0, 0xFF, 0, new RandomOptionState(0, 0, 0),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
