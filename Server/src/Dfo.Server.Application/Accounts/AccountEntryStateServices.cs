using Dfo.Server.Abstractions.Notices;
using Dfo.Server.Data.Notices;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Accounts;

internal sealed class AccountSettingsEntryProjection : IAccountSettingsEntryProjection
{
    /// <summary>The option table an account receives when it has stored none.</summary>
    /// <remarks>
    /// Seventy-five <c>u16</c> options, transcribed from the entry batch of a working A21 session.
    /// The table that was here before held sixty-seven and came from the older client: eight options
    /// short, and with different values in the ones it did carry (70 and 25 where A21 sends 100 and
    /// 100). The frame states its own byte count, so a short table is well formed and silently
    /// leaves the last eight options at whatever the client defaults them to.
    ///
    /// The values are recorded as observed. Which option each index drives is not established here,
    /// so this is a transcription rather than a set of decisions.
    /// </remarks>
    private static readonly byte[] DefaultMainGameOptions =
    [
        0x01,0x00,0x01,0x00,0x01,0x00,0x03,0x00,0x64,0x00,0x64,0x00,0x00,0x00,0x00,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x01,0x00,0x01,0x00,0x01,0x00,0x01,0x00,0x00,0x00,
        0x00,0x00,0x01,0x00,0x03,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xE8,0x03,0x00,0x00,
        0x01,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x01,0x00,0x01,0x00,0x01,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x00,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x64,0x00,0x00,0x00,0x00,0x00,0x64,0x00,
        0x00,0x00,0x00,0x00,0x01,0x00,
    ];

    public AccountSettingsEntryState Build(AccountSettingsStorageRecord storage)
    {
        if (storage.AccountHotkeySlotPrefix.Length == 1)
            throw new InvalidDataException("Account hotkey prefix is malformed.");
        var slot = storage.AccountHotkeySlotPrefix.Length >= 2
            ? BinaryPrimitives.ReadUInt16LittleEndian(storage.AccountHotkeySlotPrefix.AsSpan(0, 2))
            : (ushort?)null;
        return new AccountSettingsEntryState(
            [.. storage.MainGameOptions ?? DefaultMainGameOptions],
            [.. storage.QuickChatBank0],
            [.. storage.QuickChatBank1],
            checked((byte)storage.HotkeyKeyType),
            slot);
    }
}

internal sealed class AccountWalletEntitlementEntryProjection : IAccountWalletEntitlementEntryProjection
{
    public (AccountWalletEntryState Wallet, AccountEntitlementEntryState Entitlements) Build(
        AccountWalletEntitlementStorageRecord storage) =>
        (
            new AccountWalletEntryState(
                checked((int)storage.Cera),
                checked((int)storage.TokenCera),
                checked((int)storage.HappyTokenCera),
                checked((ushort)storage.LuckyStar)),
            new AccountEntitlementEntryState(
                storage.Premiums.OrderBy(value => value.PremiumType)
                    .Select(value => new AccountPremiumEntryState(
                        value.PremiumType, value.ExpiresAtUnixSeconds))
                    .ToArray())
        );
}

internal sealed class AccountProgressionEntryProjection : IAccountProgressionEntryProjection
{
    public AccountProgressionEntryState Build(AccountProgressionStorageRecord storage) =>
        new(
            checked((ulong)storage.HonorExperience),
            checked((uint)storage.GrowthCapsuleExperience),
            new AccountAdventureEntryState(
                storage.Roster.OrderBy(value => value.SlotIndex)
                    .ThenBy(value => value.CharacterId)
                    .Select(value => checked((byte)value.Level))
                    .ToArray()),
            storage.LotteryProgress.GroupBy(value => value.ItemTemplateId)
                .OrderBy(group => group.Key)
                .Select(group => new AccountLotteryProgressEntryState(
                    group.Key,
                    group.OrderBy(value => value.RewardIndex)
                        .Select(value => checked((byte)value.RewardIndex)).ToArray()))
                .ToArray());
}

internal sealed class AccountRelationCompatibilityEntryProjection
    : IAccountRelationCompatibilityEntryProjection
{
    public AccountRelationCompatibilityEntryState Build(
        AccountRelationCompatibilityStorageRecord storage,
        IReadOnlyList<(int CharacterId, int SlotIndex, int Level)> roster)
    {
        if (storage.PcRoomPlayTimeStates.Count == 0
            || storage.PcRoomPlayTimeStates.Distinct().Count() != 1)
            throw new InvalidDataException("Account PC-room compatibility state is inconsistent.");
        var unknown730 = ResolveAccountCompatibilityEntries(storage.Unknown730Entries, roster);
        return new AccountRelationCompatibilityEntryState(
            roster.OrderBy(value => value.SlotIndex).ThenBy(value => value.CharacterId)
                .Select(value => new AccountRosterReferenceEntryState(
                    new CharacterId(value.CharacterId), checked((ushort)value.SlotIndex)))
                .ToArray(),
            storage.Relations.Select(value => new AccountCharacterRelationEntryState(
                new CharacterId(value.OwnerCharacterId),
                checked((byte)value.Slot),
                new CharacterId(value.SupportCharacterId),
                value.SkillId,
                value.StrikerSkillId)).ToArray(),
            unknown730,
            checked((byte)storage.PcRoomPlayTimeStates[0]));
    }

    private static IReadOnlyList<AccountCompatibilityEntryState> ResolveAccountCompatibilityEntries(
        IReadOnlyList<(int CharacterSlotIndex, int SortOrder, int EntryId, int SentinelOrValue, int Flag)> entries,
        IReadOnlyList<(int CharacterId, int SlotIndex, int Level)> roster)
    {
        if (entries.Count == 0) return [];
        var bySlot = entries.GroupBy(value => value.CharacterSlotIndex)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.SortOrder).ToArray());
        var orderedSlots = roster.OrderBy(value => value.SlotIndex).Select(value => value.SlotIndex).ToArray();
        if (orderedSlots.Length == 0 || orderedSlots.Any(slot => !bySlot.ContainsKey(slot)))
            throw new InvalidDataException("Unknown730 compatibility state is not account-wide.");
        var baseline = bySlot[orderedSlots[0]];
        foreach (var slot in orderedSlots.Skip(1))
        {
            var candidate = bySlot[slot];
            if (candidate.Length != baseline.Length
                || !candidate.Select(value => (value.SortOrder, value.EntryId, value.SentinelOrValue, value.Flag))
                    .SequenceEqual(baseline.Select(value =>
                        (value.SortOrder, value.EntryId, value.SentinelOrValue, value.Flag))))
                throw new InvalidDataException("Unknown730 compatibility state differs across the account roster.");
        }
        return baseline.Select(value => new AccountCompatibilityEntryState(
            value.EntryId, value.SentinelOrValue, value.Flag)).ToArray();
    }
}

internal sealed class AccountEntryStateDomainService(
    IAccountEntryStateRepository repository,
    IAccountSettingsEntryProjection settings,
    IAccountWalletEntitlementEntryProjection walletEntitlements,
    IAccountProgressionEntryProjection progression,
    IAccountRelationCompatibilityEntryProjection relations,
    INoticeStateDomainService notices) : IAccountEntryStateDomainService
{
    public async ValueTask<AccountEntryState> LoadAsync(
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var storage = await repository.LoadAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (storage.AccountId != accountId)
            throw new InvalidDataException("Account entry storage returned a different owner.");
        var commerce = walletEntitlements.Build(storage.WalletEntitlements);
        return new AccountEntryState(
            accountId,
            await BuildSettingsAsync(accountId, storage.Settings, cancellationToken),
            commerce.Wallet,
            commerce.Entitlements,
            progression.Build(storage.Progression),
            relations.Build(storage.Relations, storage.Progression.Roster));
    }

    public async ValueTask<AccountSettingsEntryState> LoadSettingsAsync(
        AccountId accountId,
        CancellationToken cancellationToken) =>
        await BuildSettingsAsync(accountId,
            await repository.LoadSettingsAsync(accountId, cancellationToken).ConfigureAwait(false), cancellationToken);

    private async ValueTask<AccountSettingsEntryState> BuildSettingsAsync(AccountId accountId,
        AccountSettingsStorageRecord storage, CancellationToken ct) =>
        settings.Build(storage) with
        {
            SeenNotices = await notices.GetSeenAsync(NoticeOwner.ForAccount(accountId), NoticeGroup.Primary, ct),
        };
}
