using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Tests.Accounts;

public sealed class AccountEntryStateTests
{
    [Fact]
    public async Task AccountEntryState_MapsAllFourPopulatedSlicesWithoutWidthLoss()
    {
        var accountId = new AccountId(71);
        var service = new AccountEntryStateDomainService(
            new StubRepository(Storage(accountId)),
            new AccountSettingsEntryProjection(),
            new AccountWalletEntitlementEntryProjection(),
            new AccountProgressionEntryProjection(),
            new AccountRelationCompatibilityEntryProjection());

        var result = await service.LoadAsync(accountId, CancellationToken.None);

        Assert.Equal((ushort)0x1234, result.Settings.AccountHotkeySlot);
        Assert.Equal(2_147_483_647, result.Wallet.Cera);
        Assert.Equal(586, result.Entitlements.Premiums[1].PremiumType);
        Assert.Equal(6_000_000_000L, result.Entitlements.Premiums[1].ExpiresAtUnixSeconds);
        Assert.Equal(4_294_967_297UL, result.Progression.HonorExperience);
        Assert.Equal([60, 40], result.Progression.Adventure.CharacterLevels);
        Assert.Equal([3, 7], result.Progression.LotteryProgress[0].ClaimedRewardIndexes);
        Assert.Equal([501, 502], result.Relations.RosterReferences
            .Select(value => value.CharacterId.Value));
        Assert.Equal(7002, Assert.Single(result.Relations.CharacterRelations).StrikerSkillId);
        Assert.Equal(-1, result.Relations.Unknown730Entries[0].SentinelOrValue);
        Assert.Equal((byte)7, result.Relations.PcRoomPlayTimeState);
    }

    [Fact]
    public async Task AccountEntryState_RejectsRepositoryOwnerMismatch()
    {
        var service = new AccountEntryStateDomainService(
            new StubRepository(Storage(new AccountId(72))),
            new AccountSettingsEntryProjection(),
            new AccountWalletEntitlementEntryProjection(),
            new AccountProgressionEntryProjection(),
            new AccountRelationCompatibilityEntryProjection());

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await service.LoadAsync(new AccountId(71), CancellationToken.None));
    }

    [Fact]
    public void AccountSettings_UsesDemonstratedDefaultWithoutInventingQuickChatState()
    {
        var result = new AccountSettingsEntryProjection().Build(
            new AccountSettingsStorageRecord(null, [], [], 0, []));

        // Seventy-five options, which is what A21 sends. The older client's table held sixty-seven.
        Assert.Equal(150, result.MainGameOptions.Length);
        Assert.Equal([1, 0, 1, 0], result.MainGameOptions[..4]);
        Assert.Empty(result.QuickChatBank0);
        Assert.Null(result.AccountHotkeySlot);
    }

    [Fact]
    public void AccountSettings_RejectsOneByteAccountHotkeyPrefix()
    {
        Assert.Throws<InvalidDataException>(() =>
            new AccountSettingsEntryProjection().Build(
                new AccountSettingsStorageRecord([1, 0], [], [], 0, [0x34])));
    }

    [Fact]
    public async Task AccountEntryState_RejectsCharacterSpecificUnknown730AsAccountState()
    {
        var accountId = new AccountId(73);
        var storage = Storage(accountId);
        storage = storage with
        {
            Relations = storage.Relations with
            {
                Unknown730Entries =
                [
                    (0, 0, 8001, -1, 1),
                    (1, 0, 8002, -1, 1),
                ],
            },
        };
        var service = new AccountEntryStateDomainService(
            new StubRepository(storage),
            new AccountSettingsEntryProjection(),
            new AccountWalletEntitlementEntryProjection(),
            new AccountProgressionEntryProjection(),
            new AccountRelationCompatibilityEntryProjection());

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await service.LoadAsync(accountId, CancellationToken.None));
    }

    private static AccountEntryStorageRecord Storage(AccountId accountId) => new(
        accountId,
        new AccountSettingsStorageRecord(
            [1, 0], [2], [3], 1, [0x34, 0x12, 0x78, 0x56]),
        new AccountWalletEntitlementStorageRecord(
            int.MaxValue, 20, 30, ushort.MaxValue,
            [(2, 5_000_000_000L), (586, 6_000_000_000L)]),
        new AccountProgressionStorageRecord(
            4_294_967_297L, uint.MaxValue,
            [(501, 0, 60), (502, 1, 40)],
            [(1001, 7), (1001, 3), (1002, 1)]),
        new AccountRelationCompatibilityStorageRecord(
            [(501, 2, 502, 7001, 7002)],
            [(0, 0, 8001, -1, 1), (0, 1, 8002, int.MaxValue, 0),
             (1, 0, 8001, -1, 1), (1, 1, 8002, int.MaxValue, 0)],
            [7, 7]));

    private sealed class StubRepository(AccountEntryStorageRecord storage)
        : IAccountEntryStateRepository
    {
        public ValueTask<AccountEntryStorageRecord> LoadAsync(
            AccountId accountId,
            CancellationToken cancellationToken) => ValueTask.FromResult(storage);

        public ValueTask<AccountSettingsStorageRecord> LoadSettingsAsync(
            AccountId accountId,
            CancellationToken cancellationToken) => ValueTask.FromResult(storage.Settings);
    }
}
