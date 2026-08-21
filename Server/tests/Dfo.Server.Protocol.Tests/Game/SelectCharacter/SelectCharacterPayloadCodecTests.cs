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

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

public sealed class SelectCharacterPayloadCodecTests
{
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

        Assert.Equal(5 + (10 * 84), main.Length);
        Assert.Equal((byte)InventoryContainerType.Main, main[0]);
        Assert.Equal(10, BinaryPrimitives.ReadUInt16LittleEndian(main.AsSpan(3, 2)));
        Assert.Equal(1001, BinaryPrimitives.ReadInt32LittleEndian(main.AsSpan(259, 4)));
        Assert.Equal(5 + 126, avatar.Length);
        Assert.Equal(30, BinaryPrimitives.ReadInt32LittleEndian(avatar.AsSpan(89, 4)));
        Assert.Equal(0x1122, BinaryPrimitives.ReadUInt16LittleEndian(avatar.AsSpan(127, 2)));
        Assert.Equal(0x3344, BinaryPrimitives.ReadUInt16LittleEndian(avatar.AsSpan(129, 2)));
    }

    [Theory]
    [InlineData(0x006C, 3)]
    [InlineData(0x017B, 11)]
    [InlineData(0x021F, 4)]
    [InlineData(0x007C, 0)]
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
                Subtype1 = source.CharacterEntry.Public.Subtype1 with { SkillTreeIndex = 0 },
            },
        };
        var supportInventory = source.CharacterInventory with
        {
            CharacterId = supportId,
            Containers =
            [
                new InventoryContainerState(InventoryContainerType.Equipment, 0,
                [
                    new InventoryItemState(11, 1, ordinary),
                    new InventoryItemState(12, 2, avatar,
                        new InventoryAvatarDetailState(
                            checked((int)source.World.PreparedAt.AddSeconds(300).ToUnixTimeSeconds()),
                            333, Enumerable.Range(0, 30).Select(value => (byte)value).ToArray(),
                            0x1122, 0x3344, 0)),
                    new InventoryItemState(13, 24, creature, Noti2CreatureMarker: 555),
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
        Assert.Equal(320, populated.Length);
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
        Assert.Equal(2, populated[173]);
        Assert.Equal(222, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(174)));
        Assert.Equal(300u, BinaryPrimitives.ReadUInt32LittleEndian(populated.AsSpan(178)));
        Assert.Equal(333u, BinaryPrimitives.ReadUInt32LittleEndian(populated.AsSpan(185)));
        Assert.Equal(30, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(197)));
        Assert.Equal(Enumerable.Range(0, 30).Select(value => (byte)value),
            populated.AsSpan(201, 30).ToArray());
        Assert.Equal(0x1122, BinaryPrimitives.ReadUInt16LittleEndian(populated.AsSpan(235)));
        Assert.Equal(0x3344, BinaryPrimitives.ReadUInt16LittleEndian(populated.AsSpan(237)));

        // Creature slot 24 carries the NOTI-2 creature marker after the enchant block.
        Assert.Equal(24, populated[258]);
        Assert.Equal(444, BinaryPrimitives.ReadInt32LittleEndian(populated.AsSpan(259)));
        Assert.Equal(555u, BinaryPrimitives.ReadUInt32LittleEndian(populated.AsSpan(282)));
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
            (0x02D5, 2), (0x02DA, 0), (0x0300, 0), (0x0331, 0),
            (0x0357, 0), (0x0381, 0), (0x0381, 1), (0x03D8, 0),
            (0x007C, 0), (0x0111, 0), (0x01C0, 0), (0x0077, 0),
        };

        foreach (var @case in cases)
            Assert.NotNull(EncodeSelectedStateBody(
                result, @case.Type, @case.Occurrence));

        Assert.Equal(77, EncodePremiumService(result).Length);
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
        Assert.Throws<InvalidDataException>(() => EncodeSelectedStateBody(
            result with
            {
                CharacterEntry = result.CharacterEntry with
                {
                    Public = result.CharacterEntry.Public with
                    {
                        Subtype1 = result.CharacterEntry.Public.Subtype1 with { PvpResults = null! },
                    },
                },
            },
            0x0002,
            1));
    }

    [Fact]
    public void SelectCharacterPayload_AcknowledgementUsesObservedLowBytePremiumType()
    {
        var body = EncodeAcknowledgement(
            SelectCharacterPayloadTestData.CreateComplete());

        Assert.Equal(unchecked((byte)581), body[18]);
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
                            Items = [.. container.Items, new InventoryItemState(99, 24, petCore)],
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

        Assert.Equal(1, body[0]);
        Assert.Equal(0x40000001, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(2, 4)));
        Assert.Equal(321, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(15, 4)));
        Assert.Equal(444, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(19, 4)));
        Assert.Equal(1, body[23]);
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
    public void SelectCharacterPayload_Subtype1PreservesPvpResultsAndSpecialRewardQuestIds()
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();

        var body = EncodeSelectedStateBody(result, 0x0002, 1);
        byte[] expectedTail =
        [
            2,
            0x44, 0x33, 0x22, 0x11, 0x66, 0x55, 0x88, 0x77,
            0x04, 0x03, 0x02, 0x01, 0x06, 0x05, 0x08, 0x07,
            8,
            2, 0, 0, 0,
            0x0D, 0x0C, 0x0B, 0x0A,
            0x14, 0x13, 0x12, 0x11,
            0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0,
            0x0B, 0, 0, 0,
            0x0C, 0, 0, 0,
        ];

        Assert.Equal(expectedTail, body[^expectedTail.Length..]);
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
                    INSERT INTO character_subtype0_fields(character_id) VALUES(22);
                    INSERT INTO character_subtype1_fields(character_id,manage_level,skill_tree_index) VALUES(22,8,0);
                    INSERT INTO character_init_flags(character_id) VALUES(22);
                    INSERT INTO character_invisible_falgs(character_id,slot_index,flag_value)
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
            services.AddSingleton<IQuestDefinitionSource>(new Row9QuestDefinitions(13502));
            new ApplicationModule().ConfigureServices(
                services, new ConfigurationBuilder().Build());
            await using var provider = services.BuildServiceProvider();
            var typed = await provider.GetRequiredService<ICharacterEntryStateDomainService>().LoadAsync(
                new AccountId(12), new CharacterId(22), CancellationToken.None);
            var result = SelectCharacterPayloadTestData.CreateComplete() with
            {
                CharacterEntry = typed,
            };

            var body = EncodeSelectedStateBody(result, 0x0002, 1);

            Assert.Equal([13502u], typed.Public.Subtype1.SpecialRewardQuestIds);
            Assert.Empty(typed.Public.Subtype1.PvpResults);
            Assert.Equal(13502u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(body.Length - 23, 4)));
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
            result.CharacterEntry.Progression.PremiumUsageSlots).EncodeBody();

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
                    [new InventoryItemState(1, 3, core)]),
                new InventoryContainerState(InventoryContainerType.Avatar, 0,
                    [new InventoryItemState(2, 3, avatarCore,
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
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            null);
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
                    [new InventoryItemState(3, 1, equipmentCore)]),
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
        var subtype1 = new CharacterSubtype1EntryState(
            100, 90, 80, 70, 60, 50, 40, 30, 20, 120, 0, 11, 12,
            CharacterExperience: 500,
            Dimensions: [new CharacterDimensionEntryState(1, 2, 3)],
            DimensionFlags: new CharacterDimensionFlagsState(4, 5, 6, 7),
            ManageLevel: 8,
            PvpResults:
            [
                new CharacterPvpResultState(0x11223344, 0x5566, 0x7788),
                new CharacterPvpResultState(0x01020304, 0x0506, 0x0708),
            ],
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
                new CharacterSubtype0EntryState(
                    1, 2, false, false, 1, 3, false, false, 0, 4, 5, 6, 7,
                    CreatureBuffer: []),
                subtype1,
                new CharacterAppearanceCompatibilityState(1, [0]),
                4,
                5,
                new CharacterPublicIdentityEntryState(6, 7, 8)),
            new CharacterSettingsEntryState([1, 2], [9, 8], 4),
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
