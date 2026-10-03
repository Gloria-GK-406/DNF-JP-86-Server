using System.Buffers.Binary;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// Everything a character's and an account's inventory is loaded with, written down before it is
/// moved.
/// </summary>
/// <remarks>
/// The two loads read fourteen tables between them and assemble two records. The tests already on
/// this path assert the values someone thought to name, and the refusals; none of them notices a
/// column that quietly swaps with its neighbour, a collection that reorders, or a field that
/// stops being filled in. Splitting these reads into a repository per table is exactly the change
/// that could do any of those, so the whole of both records is written out and compared as text.
///
/// The seed is chosen so that no two values which could be confused with each other are equal:
/// every column gets a distinct number, and the rows are laid out so that id order, slot order and
/// insertion order all differ. A golden over a seed of zeroes would pass through any permutation
/// of the columns it covers.
///
/// The first line of every item was a storage entry id and is now a name, and every one of them
/// reads null. Nothing was lost by that: the id was a surrogate this server never sends, which
/// appears in no frame and only in the text of a refusal, and it changed once already when a
/// character's items and an account's cargo became one table with one count. What stands in its
/// place is the name of the thing on the row, which a thing keeps across every write of it. The
/// archive this side is given declares every item an ordinary consumable, and two of a consumable
/// are the same thing, so none of these rows has a name to carry -- a pile with one would be
/// carrying exactly the mark somebody could go on to tell two identical piles apart by.
///
/// Every other line of both goldens is unchanged, which is what says that only this moved.
/// </remarks>
public sealed class InventoryLoadCharacterizationTests
{
    private const int Account = 41;
    private const int Character = 57;

    [Fact]
    public async Task ACharacterInventoryIsLoadedWithExactlyWhatItWasLoadedWithBefore()
    {
        var path = TempPath();
        try
        {
            var side = new InventorySide(path);
            await SeedAsync(path);

            var loaded = await side.InUseCaseAsync(token => side.CharacterEntries
                .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token));

            Assert.Equal(
                ExpectedCharacter.ReplaceLineEndings("\n") + "\n", StorageRecordDump.Of(loaded));
        }
        finally
        {
            Clean(path);
        }
    }

    [Fact]
    public async Task AnAccountCargoIsLoadedWithExactlyWhatItWasLoadedWithBefore()
    {
        var path = TempPath();
        try
        {
            var side = new InventorySide(path);
            await SeedAsync(path);

            var loaded = await side.InUseCaseAsync(token => side.AccountEntries
                .LoadAccountAsync(new AccountId(Account), new CharacterId(Character), token));

            Assert.Equal(
                ExpectedAccount.ReplaceLineEndings("\n") + "\n", StorageRecordDump.Of(loaded));
        }
        finally
        {
            Clean(path);
        }
    }

    /// <summary>
    /// An avatar sitting in the account's cargo is carried with none of an avatar's facts.
    /// </summary>
    /// <remarks>
    /// An avatar's facts were only ever a character's, and a cargo row's owner is an account, so a
    /// cargo row has never had any. The load has never looked, and looking would not find them --
    /// it would refuse the whole cargo.
    ///
    /// The golden above cannot say this, because the cargo it seeds holds no avatar: both a load
    /// that looks and one that does not print the same absent detail for an ordinary item. So it
    /// is said here, with an avatar actually in the cargo.
    /// </remarks>
    [Fact]
    public async Task AnAvatarInTheCargoIsCarriedWithoutADetailRow()
    {
        var path = TempPath();
        try
        {
            var side = new InventorySide(path);
            await SeedAsync(path);
            await InsertCargoAvatarAsync(path, slot: 5, from: 110, itemId: 2700, avatarUid: 5151);

            var loaded = await side.InUseCaseAsync(token => side.AccountEntries
                .LoadAccountAsync(new AccountId(Account), new CharacterId(Character), token));

            var avatar = Assert.Single(
                loaded.AccountCargoItems, item => item.Core.ItemKind == 8);
            Assert.Equal(5151, avatar.Core.Value);
            Assert.Null(avatar.AvatarDetail);
        }
        finally
        {
            Clean(path);
        }
    }

    private const string ExpectedCharacter = """
        AccountId.Value = 41
        CharacterId.Value = 57
        CharacterItems[0].Identity = null
        CharacterItems[0].OwnerType = Character
        CharacterItems[0].OwnerId = 57
        CharacterItems[0].CharacterId.Value = 57
        CharacterItems[0].ContainerType = Main
        CharacterItems[0].SlotIndex = 0
        CharacterItems[0].Core.ItemKind = 1
        CharacterItems[0].Core.ItemId = 2001
        CharacterItems[0].Core.Value = 33
        CharacterItems[0].Core.Attribute = 139
        CharacterItems[0].Core.Durability = 36236
        CharacterItems[0].Core.SealFlag = 142
        CharacterItems[0].Core.EnchantCardId = -1835954033
        CharacterItems[0].Core.EnchantUpgradeCount = 147
        CharacterItems[0].Core.AmplifyType = 148
        CharacterItems[0].Core.AmplifyValue = 38549
        CharacterItems[0].Core.Marker16 = -1701209961
        CharacterItems[0].Core.ChronicleOption0.Id = -1633837925
        CharacterItems[0].Core.ChronicleOption0.CharacterJob = 159
        CharacterItems[0].Core.ChronicleOption0.FirstGrowType = 160
        CharacterItems[0].Core.ChronicleOption0.EquipmentType = 161
        CharacterItems[0].Core.ChronicleOption0.OptionNumber = 162
        CharacterItems[0].Core.ChronicleOption1.Id = -1499093853
        CharacterItems[0].Core.ChronicleOption1.CharacterJob = 167
        CharacterItems[0].Core.ChronicleOption1.FirstGrowType = 168
        CharacterItems[0].Core.ChronicleOption1.EquipmentType = 169
        CharacterItems[0].Core.ChronicleOption1.OptionNumber = 170
        CharacterItems[0].Core.ExpireTime = -1364349781
        CharacterItems[0].Core.EmblemSocketCount = 175
        CharacterItems[0].Core.EmblemId1 = -1280134736
        CharacterItems[0].Core.EmblemId2 = -1212762700
        CharacterItems[0].Core.Rune = 47544
        CharacterItems[0].Core.RandomOption0.Type = 186
        CharacterItems[0].Core.RandomOption0.Value1 = 187
        CharacterItems[0].Core.RandomOption0.Value2 = 188
        CharacterItems[0].Core.RandomOption1.Type = 189
        CharacterItems[0].Core.RandomOption1.Value1 = 190
        CharacterItems[0].Core.RandomOption1.Value2 = 191
        CharacterItems[0].Core.RandomOption2.Type = 192
        CharacterItems[0].Core.RandomOption2.Value1 = 193
        CharacterItems[0].Core.RandomOption2.Value2 = 194
        CharacterItems[0].Core.RandomOptionState = 195
        CharacterItems[0].Core.RandomOptionChangedIndex = 196
        CharacterItems[0].Core.RandomOptionChangeState = 197
        CharacterItems[0].Core.RandomOptionChange.Type = 198
        CharacterItems[0].Core.RandomOptionChange.Value1 = 199
        CharacterItems[0].Core.RandomOptionChange.Value2 = 200
        CharacterItems[0].Core.GenuineUpgrade = 201
        CharacterItems[0].Core.EmancipateEquipmentLevel = 202
        CharacterItems[0].Core.TradeRestriction = 203
        CharacterItems[0].Core.TailUnknown0 = 52684
        CharacterItems[0].Core.TailUnknown1 = 206
        CharacterItems[0].Core.TailUnknown2 = 207
        CharacterItems[0].Core.TailUnknown3 = 208
        CharacterItems[0].Core.RemainUseCount = 209
        CharacterItems[0].Core.SortLockFlag = 210
        CharacterItems[0].Core.EquipmentLockId = 211
        CharacterItems[0].Core.ReinforcementLevel = 11
        CharacterItems[0].AvatarDetail = null
        CharacterItems[1].Identity = null
        CharacterItems[1].OwnerType = Character
        CharacterItems[1].OwnerId = 57
        CharacterItems[1].CharacterId.Value = 57
        CharacterItems[1].ContainerType = Main
        CharacterItems[1].SlotIndex = 1
        CharacterItems[1].Core.ItemKind = 1
        CharacterItems[1].Core.ItemId = 2002
        CharacterItems[1].Core.Value = -5
        CharacterItems[1].Core.Attribute = 169
        CharacterItems[1].Core.Durability = 43946
        CharacterItems[1].Core.SealFlag = 172
        CharacterItems[1].Core.EnchantCardId = -1330663763
        CharacterItems[1].Core.EnchantUpgradeCount = 177
        CharacterItems[1].Core.AmplifyType = 178
        CharacterItems[1].Core.AmplifyValue = 46259
        CharacterItems[1].Core.Marker16 = -1195919691
        CharacterItems[1].Core.ChronicleOption0.Id = -1128547655
        CharacterItems[1].Core.ChronicleOption0.CharacterJob = 189
        CharacterItems[1].Core.ChronicleOption0.FirstGrowType = 190
        CharacterItems[1].Core.ChronicleOption0.EquipmentType = 191
        CharacterItems[1].Core.ChronicleOption0.OptionNumber = 192
        CharacterItems[1].Core.ChronicleOption1.Id = -993803583
        CharacterItems[1].Core.ChronicleOption1.CharacterJob = 197
        CharacterItems[1].Core.ChronicleOption1.FirstGrowType = 198
        CharacterItems[1].Core.ChronicleOption1.EquipmentType = 199
        CharacterItems[1].Core.ChronicleOption1.OptionNumber = 200
        CharacterItems[1].Core.ExpireTime = -859059511
        CharacterItems[1].Core.EmblemSocketCount = 205
        CharacterItems[1].Core.EmblemId1 = -774844466
        CharacterItems[1].Core.EmblemId2 = -707472430
        CharacterItems[1].Core.Rune = 55254
        CharacterItems[1].Core.RandomOption0.Type = 216
        CharacterItems[1].Core.RandomOption0.Value1 = 217
        CharacterItems[1].Core.RandomOption0.Value2 = 218
        CharacterItems[1].Core.RandomOption1.Type = 219
        CharacterItems[1].Core.RandomOption1.Value1 = 220
        CharacterItems[1].Core.RandomOption1.Value2 = 221
        CharacterItems[1].Core.RandomOption2.Type = 222
        CharacterItems[1].Core.RandomOption2.Value1 = 223
        CharacterItems[1].Core.RandomOption2.Value2 = 224
        CharacterItems[1].Core.RandomOptionState = 225
        CharacterItems[1].Core.RandomOptionChangedIndex = 226
        CharacterItems[1].Core.RandomOptionChangeState = 227
        CharacterItems[1].Core.RandomOptionChange.Type = 228
        CharacterItems[1].Core.RandomOptionChange.Value1 = 229
        CharacterItems[1].Core.RandomOptionChange.Value2 = 230
        CharacterItems[1].Core.GenuineUpgrade = 231
        CharacterItems[1].Core.EmancipateEquipmentLevel = 232
        CharacterItems[1].Core.TradeRestriction = 233
        CharacterItems[1].Core.TailUnknown0 = 60394
        CharacterItems[1].Core.TailUnknown1 = 236
        CharacterItems[1].Core.TailUnknown2 = 237
        CharacterItems[1].Core.TailUnknown3 = 238
        CharacterItems[1].Core.RemainUseCount = 239
        CharacterItems[1].Core.SortLockFlag = 240
        CharacterItems[1].Core.EquipmentLockId = 241
        CharacterItems[1].Core.ReinforcementLevel = 9
        CharacterItems[1].AvatarDetail = null
        CharacterItems[2].Identity = null
        CharacterItems[2].OwnerType = Character
        CharacterItems[2].OwnerId = 57
        CharacterItems[2].CharacterId.Value = 57
        CharacterItems[2].ContainerType = Main
        CharacterItems[2].SlotIndex = 2
        CharacterItems[2].Core.ItemKind = 1
        CharacterItems[2].Core.ItemId = 2003
        CharacterItems[2].Core.Value = 31
        CharacterItems[2].Core.Attribute = 109
        CharacterItems[2].Core.Durability = 28526
        CharacterItems[2].Core.SealFlag = 112
        CharacterItems[2].Core.EnchantCardId = 1953722993
        CharacterItems[2].Core.EnchantUpgradeCount = 117
        CharacterItems[2].Core.AmplifyType = 118
        CharacterItems[2].Core.AmplifyValue = 30839
        CharacterItems[2].Core.Marker16 = 2088467065
        CharacterItems[2].Core.ChronicleOption0.Id = -2139128195
        CharacterItems[2].Core.ChronicleOption0.CharacterJob = 129
        CharacterItems[2].Core.ChronicleOption0.FirstGrowType = 130
        CharacterItems[2].Core.ChronicleOption0.EquipmentType = 131
        CharacterItems[2].Core.ChronicleOption0.OptionNumber = 132
        CharacterItems[2].Core.ChronicleOption1.Id = -2004384123
        CharacterItems[2].Core.ChronicleOption1.CharacterJob = 137
        CharacterItems[2].Core.ChronicleOption1.FirstGrowType = 138
        CharacterItems[2].Core.ChronicleOption1.EquipmentType = 139
        CharacterItems[2].Core.ChronicleOption1.OptionNumber = 140
        CharacterItems[2].Core.ExpireTime = -1869640051
        CharacterItems[2].Core.EmblemSocketCount = 145
        CharacterItems[2].Core.EmblemId1 = -1785425006
        CharacterItems[2].Core.EmblemId2 = -1718052970
        CharacterItems[2].Core.Rune = 39834
        CharacterItems[2].Core.RandomOption0.Type = 156
        CharacterItems[2].Core.RandomOption0.Value1 = 157
        CharacterItems[2].Core.RandomOption0.Value2 = 158
        CharacterItems[2].Core.RandomOption1.Type = 159
        CharacterItems[2].Core.RandomOption1.Value1 = 160
        CharacterItems[2].Core.RandomOption1.Value2 = 161
        CharacterItems[2].Core.RandomOption2.Type = 162
        CharacterItems[2].Core.RandomOption2.Value1 = 163
        CharacterItems[2].Core.RandomOption2.Value2 = 164
        CharacterItems[2].Core.RandomOptionState = 165
        CharacterItems[2].Core.RandomOptionChangedIndex = 166
        CharacterItems[2].Core.RandomOptionChangeState = 167
        CharacterItems[2].Core.RandomOptionChange.Type = 168
        CharacterItems[2].Core.RandomOptionChange.Value1 = 169
        CharacterItems[2].Core.RandomOptionChange.Value2 = 170
        CharacterItems[2].Core.GenuineUpgrade = 171
        CharacterItems[2].Core.EmancipateEquipmentLevel = 172
        CharacterItems[2].Core.TradeRestriction = 173
        CharacterItems[2].Core.TailUnknown0 = 44974
        CharacterItems[2].Core.TailUnknown1 = 176
        CharacterItems[2].Core.TailUnknown2 = 177
        CharacterItems[2].Core.TailUnknown3 = 178
        CharacterItems[2].Core.RemainUseCount = 179
        CharacterItems[2].Core.SortLockFlag = 180
        CharacterItems[2].Core.EquipmentLockId = 181
        CharacterItems[2].Core.ReinforcementLevel = 13
        CharacterItems[2].AvatarDetail = null
        CharacterItems[3].Identity = null
        CharacterItems[3].OwnerType = Character
        CharacterItems[3].OwnerId = 57
        CharacterItems[3].CharacterId.Value = 57
        CharacterItems[3].ContainerType = Avatar
        CharacterItems[3].SlotIndex = 9
        CharacterItems[3].Core.ItemKind = 8
        CharacterItems[3].Core.ItemId = 2500
        CharacterItems[3].Core.Value = 4242
        CharacterItems[3].Core.Attribute = 49
        CharacterItems[3].Core.Durability = 13106
        CharacterItems[3].Core.SealFlag = 52
        CharacterItems[3].Core.EnchantCardId = 943142453
        CharacterItems[3].Core.EnchantUpgradeCount = 57
        CharacterItems[3].Core.AmplifyType = 58
        CharacterItems[3].Core.AmplifyValue = 15419
        CharacterItems[3].Core.Marker16 = 1077886525
        CharacterItems[3].Core.ChronicleOption0.Id = 1145258561
        CharacterItems[3].Core.ChronicleOption0.CharacterJob = 69
        CharacterItems[3].Core.ChronicleOption0.FirstGrowType = 70
        CharacterItems[3].Core.ChronicleOption0.EquipmentType = 71
        CharacterItems[3].Core.ChronicleOption0.OptionNumber = 72
        CharacterItems[3].Core.ChronicleOption1.Id = 1280002633
        CharacterItems[3].Core.ChronicleOption1.CharacterJob = 77
        CharacterItems[3].Core.ChronicleOption1.FirstGrowType = 78
        CharacterItems[3].Core.ChronicleOption1.EquipmentType = 79
        CharacterItems[3].Core.ChronicleOption1.OptionNumber = 80
        CharacterItems[3].Core.ExpireTime = 1414746705
        CharacterItems[3].Core.EmblemSocketCount = 85
        CharacterItems[3].Core.EmblemId1 = 1498961750
        CharacterItems[3].Core.EmblemId2 = 1566333786
        CharacterItems[3].Core.Rune = 24414
        CharacterItems[3].Core.RandomOption0.Type = 96
        CharacterItems[3].Core.RandomOption0.Value1 = 97
        CharacterItems[3].Core.RandomOption0.Value2 = 98
        CharacterItems[3].Core.RandomOption1.Type = 99
        CharacterItems[3].Core.RandomOption1.Value1 = 100
        CharacterItems[3].Core.RandomOption1.Value2 = 101
        CharacterItems[3].Core.RandomOption2.Type = 102
        CharacterItems[3].Core.RandomOption2.Value1 = 103
        CharacterItems[3].Core.RandomOption2.Value2 = 104
        CharacterItems[3].Core.RandomOptionState = 105
        CharacterItems[3].Core.RandomOptionChangedIndex = 106
        CharacterItems[3].Core.RandomOptionChangeState = 107
        CharacterItems[3].Core.RandomOptionChange.Type = 108
        CharacterItems[3].Core.RandomOptionChange.Value1 = 109
        CharacterItems[3].Core.RandomOptionChange.Value2 = 110
        CharacterItems[3].Core.GenuineUpgrade = 111
        CharacterItems[3].Core.EmancipateEquipmentLevel = 112
        CharacterItems[3].Core.TradeRestriction = 113
        CharacterItems[3].Core.TailUnknown0 = 29554
        CharacterItems[3].Core.TailUnknown1 = 116
        CharacterItems[3].Core.TailUnknown2 = 117
        CharacterItems[3].Core.TailUnknown3 = 118
        CharacterItems[3].Core.RemainUseCount = 119
        CharacterItems[3].Core.SortLockFlag = 120
        CharacterItems[3].Core.EquipmentLockId = 121
        CharacterItems[3].Core.ReinforcementLevel = 17
        CharacterItems[3].AvatarDetail.ExpireDate = 7001
        CharacterItems[3].AvatarDetail.ClearAvatarId = 7002
        CharacterItems[3].AvatarDetail.JewelSocket = 0xC9CACBCCCDCECFD0D1D2D3D4D5D6D7D8D9DADBDCDDDEDFE0E1E2E3E4E5E6
        CharacterItems[3].AvatarDetail.Color1 = 7003
        CharacterItems[3].AvatarDetail.Color2 = 7004
        CharacterItems[3].AvatarDetail.DeleteDate = 7005
        CharacterContainerParameters[0].CharacterId.Value = 57
        CharacterContainerParameters[0].ContainerType = Main
        CharacterContainerParameters[0].Value = 3333
        CharacterContainerParameters[1].CharacterId.Value = 57
        CharacterContainerParameters[1].ContainerType = Avatar
        CharacterContainerParameters[1].Value = 4444
        CharacterVirtualResources[0].SlotIndex = 0
        CharacterVirtualResources[0].ItemId = 0
        CharacterVirtualResources[0].Value = 0
        CharacterVirtualResources[1].SlotIndex = 1
        CharacterVirtualResources[1].ItemId = 2002
        CharacterVirtualResources[1].Value = 0
        CharacterVirtualResources[2].SlotIndex = 2
        CharacterVirtualResources[2].ItemId = 2003
        CharacterVirtualResources[2].Value = 31
        CharacterItemValues[0].CharacterId.Value = 57
        CharacterItemValues[0].ListType = Cooldown
        CharacterItemValues[0].SortOrder = 0
        CharacterItemValues[0].ItemId = 5301
        CharacterItemValues[0].Value = 5302
        CharacterItemValues[1].CharacterId.Value = 57
        CharacterItemValues[1].ListType = Cooldown
        CharacterItemValues[1].SortOrder = 1
        CharacterItemValues[1].ItemId = 5201
        CharacterItemValues[1].Value = 5202
        CharacterItemValues[2].CharacterId.Value = 57
        CharacterItemValues[2].ListType = Effect
        CharacterItemValues[2].SortOrder = 0
        CharacterItemValues[2].ItemId = 5101
        CharacterItemValues[2].Value = 5102
        EquipmentLocks[0].CharacterId.Value = 57
        EquipmentLocks[0].EquipmentLockId = 2
        EquipmentLocks[0].ContainerType = Avatar
        EquipmentLocks[0].SlotIndex = 61
        EquipmentLocks[0].State = 71
        EquipmentLocks[0].RemainingSeconds = 81
        CharacterRentals[0].CharacterId.Value = 57
        CharacterRentals[0].ItemId = 9001
        CharacterRentals[0].InventoryTemplateId = 9101
        CharacterRentals[0].ExpireTime = 9201
        CharacterRentals[1].CharacterId.Value = 57
        CharacterRentals[1].ItemId = 9002
        CharacterRentals[1].InventoryTemplateId = 9102
        CharacterRentals[1].ExpireTime = 9202
        GoldLimits.CarryLimit = 7777
        GoldLimits.AuctionLimit = 8888
        """;

    private const string ExpectedAccount = """
        AccountId.Value = 41
        CharacterId.Value = 57
        AccountCargoItems[0].Identity = null
        AccountCargoItems[0].OwnerType = Account
        AccountCargoItems[0].OwnerId = 41
        AccountCargoItems[0].CharacterId.Value = 57
        AccountCargoItems[0].ContainerType = AccountCargo
        AccountCargoItems[0].SlotIndex = 3
        AccountCargoItems[0].Core.ItemKind = 1
        AccountCargoItems[0].Core.ItemId = 2601
        AccountCargoItems[0].Core.Value = 62
        AccountCargoItems[0].Core.Attribute = 89
        AccountCargoItems[0].Core.Durability = 23386
        AccountCargoItems[0].Core.SealFlag = 92
        AccountCargoItems[0].Core.EnchantCardId = 1616862813
        AccountCargoItems[0].Core.EnchantUpgradeCount = 97
        AccountCargoItems[0].Core.AmplifyType = 98
        AccountCargoItems[0].Core.AmplifyValue = 25699
        AccountCargoItems[0].Core.Marker16 = 1751606885
        AccountCargoItems[0].Core.ChronicleOption0.Id = 1818978921
        AccountCargoItems[0].Core.ChronicleOption0.CharacterJob = 109
        AccountCargoItems[0].Core.ChronicleOption0.FirstGrowType = 110
        AccountCargoItems[0].Core.ChronicleOption0.EquipmentType = 111
        AccountCargoItems[0].Core.ChronicleOption0.OptionNumber = 112
        AccountCargoItems[0].Core.ChronicleOption1.Id = 1953722993
        AccountCargoItems[0].Core.ChronicleOption1.CharacterJob = 117
        AccountCargoItems[0].Core.ChronicleOption1.FirstGrowType = 118
        AccountCargoItems[0].Core.ChronicleOption1.EquipmentType = 119
        AccountCargoItems[0].Core.ChronicleOption1.OptionNumber = 120
        AccountCargoItems[0].Core.ExpireTime = 2088467065
        AccountCargoItems[0].Core.EmblemSocketCount = 125
        AccountCargoItems[0].Core.EmblemId1 = -2122285186
        AccountCargoItems[0].Core.EmblemId2 = -2054913150
        AccountCargoItems[0].Core.Rune = 34694
        AccountCargoItems[0].Core.RandomOption0.Type = 136
        AccountCargoItems[0].Core.RandomOption0.Value1 = 137
        AccountCargoItems[0].Core.RandomOption0.Value2 = 138
        AccountCargoItems[0].Core.RandomOption1.Type = 139
        AccountCargoItems[0].Core.RandomOption1.Value1 = 140
        AccountCargoItems[0].Core.RandomOption1.Value2 = 141
        AccountCargoItems[0].Core.RandomOption2.Type = 142
        AccountCargoItems[0].Core.RandomOption2.Value1 = 143
        AccountCargoItems[0].Core.RandomOption2.Value2 = 144
        AccountCargoItems[0].Core.RandomOptionState = 145
        AccountCargoItems[0].Core.RandomOptionChangedIndex = 146
        AccountCargoItems[0].Core.RandomOptionChangeState = 147
        AccountCargoItems[0].Core.RandomOptionChange.Type = 148
        AccountCargoItems[0].Core.RandomOptionChange.Value1 = 149
        AccountCargoItems[0].Core.RandomOptionChange.Value2 = 150
        AccountCargoItems[0].Core.GenuineUpgrade = 151
        AccountCargoItems[0].Core.EmancipateEquipmentLevel = 152
        AccountCargoItems[0].Core.TradeRestriction = 153
        AccountCargoItems[0].Core.TailUnknown0 = 39834
        AccountCargoItems[0].Core.TailUnknown1 = 156
        AccountCargoItems[0].Core.TailUnknown2 = 157
        AccountCargoItems[0].Core.TailUnknown3 = 158
        AccountCargoItems[0].Core.RemainUseCount = 159
        AccountCargoItems[0].Core.SortLockFlag = 160
        AccountCargoItems[0].Core.EquipmentLockId = 161
        AccountCargoItems[0].Core.ReinforcementLevel = 25
        AccountCargoItems[0].AvatarDetail = null
        AccountCargoItems[1].Identity = null
        AccountCargoItems[1].OwnerType = Account
        AccountCargoItems[1].OwnerId = 41
        AccountCargoItems[1].CharacterId.Value = 57
        AccountCargoItems[1].ContainerType = AccountCargo
        AccountCargoItems[1].SlotIndex = 4
        AccountCargoItems[1].Core.ItemKind = 1
        AccountCargoItems[1].Core.ItemId = 2600
        AccountCargoItems[1].Core.Value = 61
        AccountCargoItems[1].Core.Attribute = 69
        AccountCargoItems[1].Core.Durability = 18246
        AccountCargoItems[1].Core.SealFlag = 72
        AccountCargoItems[1].Core.EnchantCardId = 1280002633
        AccountCargoItems[1].Core.EnchantUpgradeCount = 77
        AccountCargoItems[1].Core.AmplifyType = 78
        AccountCargoItems[1].Core.AmplifyValue = 20559
        AccountCargoItems[1].Core.Marker16 = 1414746705
        AccountCargoItems[1].Core.ChronicleOption0.Id = 1482118741
        AccountCargoItems[1].Core.ChronicleOption0.CharacterJob = 89
        AccountCargoItems[1].Core.ChronicleOption0.FirstGrowType = 90
        AccountCargoItems[1].Core.ChronicleOption0.EquipmentType = 91
        AccountCargoItems[1].Core.ChronicleOption0.OptionNumber = 92
        AccountCargoItems[1].Core.ChronicleOption1.Id = 1616862813
        AccountCargoItems[1].Core.ChronicleOption1.CharacterJob = 97
        AccountCargoItems[1].Core.ChronicleOption1.FirstGrowType = 98
        AccountCargoItems[1].Core.ChronicleOption1.EquipmentType = 99
        AccountCargoItems[1].Core.ChronicleOption1.OptionNumber = 100
        AccountCargoItems[1].Core.ExpireTime = 1751606885
        AccountCargoItems[1].Core.EmblemSocketCount = 105
        AccountCargoItems[1].Core.EmblemId1 = 1835821930
        AccountCargoItems[1].Core.EmblemId2 = 1903193966
        AccountCargoItems[1].Core.Rune = 29554
        AccountCargoItems[1].Core.RandomOption0.Type = 116
        AccountCargoItems[1].Core.RandomOption0.Value1 = 117
        AccountCargoItems[1].Core.RandomOption0.Value2 = 118
        AccountCargoItems[1].Core.RandomOption1.Type = 119
        AccountCargoItems[1].Core.RandomOption1.Value1 = 120
        AccountCargoItems[1].Core.RandomOption1.Value2 = 121
        AccountCargoItems[1].Core.RandomOption2.Type = 122
        AccountCargoItems[1].Core.RandomOption2.Value1 = 123
        AccountCargoItems[1].Core.RandomOption2.Value2 = 124
        AccountCargoItems[1].Core.RandomOptionState = 125
        AccountCargoItems[1].Core.RandomOptionChangedIndex = 126
        AccountCargoItems[1].Core.RandomOptionChangeState = 127
        AccountCargoItems[1].Core.RandomOptionChange.Type = 128
        AccountCargoItems[1].Core.RandomOptionChange.Value1 = 129
        AccountCargoItems[1].Core.RandomOptionChange.Value2 = 130
        AccountCargoItems[1].Core.GenuineUpgrade = 131
        AccountCargoItems[1].Core.EmancipateEquipmentLevel = 132
        AccountCargoItems[1].Core.TradeRestriction = 133
        AccountCargoItems[1].Core.TailUnknown0 = 34694
        AccountCargoItems[1].Core.TailUnknown1 = 136
        AccountCargoItems[1].Core.TailUnknown2 = 137
        AccountCargoItems[1].Core.TailUnknown3 = 138
        AccountCargoItems[1].Core.RemainUseCount = 139
        AccountCargoItems[1].Core.SortLockFlag = 140
        AccountCargoItems[1].Core.EquipmentLockId = 141
        AccountCargoItems[1].Core.ReinforcementLevel = 5
        AccountCargoItems[1].AvatarDetail = null
        AccountCargoState.AccountId.Value = 41
        AccountCargoState.SelectionKey = 6001
        AccountCargoState.Value32 = 6002
        AccountCargoState.StoredItemCount = 6003
        AccountVirtualResources[0].SlotIndex = 354
        AccountVirtualResources[0].ItemId = 3033
        AccountVirtualResources[0].Value = 11
        AccountVirtualResources[1].SlotIndex = 355
        AccountVirtualResources[1].ItemId = 3034
        AccountVirtualResources[1].Value = 12
        AccountVirtualResources[2].SlotIndex = 357
        AccountVirtualResources[2].ItemId = 3036
        AccountVirtualResources[2].Value = 14
        AccountVirtualResources[3].SlotIndex = 358
        AccountVirtualResources[3].ItemId = 3037
        AccountVirtualResources[3].Value = 15
        AccountVirtualResources[4].SlotIndex = 359
        AccountVirtualResources[4].ItemId = 3262
        AccountVirtualResources[4].Value = 16
        EquipmentLocks[0].CharacterId.Value = 57
        EquipmentLocks[0].EquipmentLockId = 1
        EquipmentLocks[0].ContainerType = AccountCargo
        EquipmentLocks[0].SlotIndex = 62
        EquipmentLocks[0].State = 72
        EquipmentLocks[0].RemainingSeconds = 0
        """;

    /// <summary>
    /// One of everything the two loads can reach, with nothing in it repeated.
    /// </summary>
    /// <remarks>
    /// The item cores are built rather than written out: eighty-two bytes each, and what matters
    /// about one is that every byte in it differs from every other, which a counter gives and a
    /// hand-written constant would not.
    ///
    /// The rows are deliberately awkward. Item ids run opposite to slot order, so a query that
    /// lost its ORDER BY would show. Two containers are in play, so container order is visible.
    /// The locks table holds one row for a character container and one for account cargo, because
    /// each of the two loads keeps only its own half of that table.
    /// </remarks>
    private static async Task SeedAsync(string path)
    {
        await ExecuteAsync(path, $"""
            INSERT INTO accounts(account_id,m_id)
            VALUES({Account},'pin');
            INSERT INTO characters(character_id,account_id,name,job,level)
            VALUES({Character},{Account},'pin',3,60);

            INSERT INTO character_container_state(character_id,list_type,list_param16)
            VALUES({Character},1,4444),({Character},0,3333);

            INSERT INTO character_stats(character_id,gold_carry_limit,auction_gold_limit)
            VALUES({Character},7777,8888);

            INSERT INTO character_item_cooldowns(character_id,sort_order,item_id,value)
            VALUES({Character},1,5201,5202),
                  ({Character},0,5301,5302);
            INSERT INTO character_item_effects(character_id,sort_order,item_id,value)
            VALUES({Character},0,5101,5102);

            INSERT INTO character_item_locks(character_id,equipment_lock_id,inventory_list_type,slot,state,remaining_seconds)
            VALUES({Character},2,1,61,71,81),
                  ({Character},1,12,62,72,NULL);

            INSERT INTO character_rentals(character_id,item_id,inventory_template_id,expire_time)
            VALUES({Character},9001,9101,9201),
                  ({Character},9002,9102,9202);

            INSERT INTO account_cargo_state(account_id,selection_key,value32,item_count)
            VALUES({Account},6001,6002,6003);
            """);

        foreach (var (slot, count) in new[] { (0, 11), (1, 12), (3, 14), (4, 15), (5, 16) })
        {
            var material = AccountMaterialCatalog.Definitions[slot];
            var core = Convert.ToHexString(InventoryItemCore.Encode(StackableItemCore.Create(material.ItemKind, material.ItemId, count)));
            await ExecuteAsync(path, $"""
                INSERT INTO items(item_core) VALUES(X'{core}');
                INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                VALUES('account',{Account},240,{slot},'template',{material.ItemId},last_insert_rowid(),{count});
                """);
        }

        // Slots 0 to 2 of the main container become the runtime values; their ids run downward so
        // their order cannot have come from the order the rows went in.
        await InsertCharacterItemAsync(path, listType: 0, slot: 2, from: 100, itemKind: 1, itemId: 2003, value: 31);
        await InsertCharacterItemAsync(path, listType: 0, slot: 0, from: 130, itemKind: 1, itemId: 2001, value: 33);
        await InsertCharacterItemAsync(path, listType: 0, slot: 1, from: 160, itemKind: 1, itemId: 2002, value: -5);
        // The avatar and what it carries are one row now, so they go in as one statement.
        await ExecuteAsync(path, $"""
            INSERT INTO items(item_core,
              avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,avatar_color2,
              avatar_delete_date)
            VALUES({Core(40, 8, 2500, 4242)},
              7001,7002,{Socket()},7003,7004,7005);
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',{Character},1,9,'world',last_insert_rowid(),
              last_insert_rowid(),1);
            """);

        await InsertCargoItemAsync(path, slot: 4, from: 60, itemId: 2600, value: 61);
        await InsertCargoItemAsync(path, slot: 3, from: 80, itemId: 2601, value: 62);
    }

    /// <summary>An item core whose every byte differs from every other byte in it.</summary>
    private static string Core(int from, byte itemKind, int itemId, int value)
    {
        var core = new byte[82];
        for (var index = 0; index < core.Length; index++)
        {
            core[index] = checked((byte)(from + index));
        }

        core[0] = itemKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), value);
        return "X'" + Convert.ToHexString(core) + "'";
    }

    private static string Socket()
    {
        var socket = new byte[30];
        for (var index = 0; index < socket.Length; index++)
        {
            socket[index] = checked((byte)(201 + index));
        }

        return "X'" + Convert.ToHexString(socket) + "'";
    }

    private static Task InsertCharacterItemAsync(
        string path, int listType, int slot, int from, byte itemKind, int itemId, int value) =>
        ExecuteAsync(path, $"""
            INSERT INTO items(item_core)
            VALUES({Core(from, itemKind, itemId, value)});
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',{Character},{listType},{slot},'world',last_insert_rowid(),
              last_insert_rowid(),1);
            """);

    private static Task InsertCargoAvatarAsync(
        string path, int slot, int from, int itemId, int avatarUid) =>
        ExecuteAsync(path, $"""
            INSERT INTO items(cargo_character_id,item_core)
            VALUES({Character},{Core(from, 8, itemId, avatarUid)});
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('account',{Account},12,{slot},'world',last_insert_rowid(),
              last_insert_rowid(),1);
            """);

    private static Task InsertCargoItemAsync(
        string path, int slot, int from, int itemId, int value) =>
        ExecuteAsync(path, $"""
            INSERT INTO items(cargo_character_id,item_core)
            VALUES({Character},{Core(from, 1, itemId, value)});
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('account',{Account},12,{slot},'world',last_insert_rowid(),
              last_insert_rowid(),1);
            """);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"dfo-inv-pin-{Guid.NewGuid():N}.db");

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void Clean(string path)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A file the runtime still holds is left for the temp directory to reap.
        }
    }
}
