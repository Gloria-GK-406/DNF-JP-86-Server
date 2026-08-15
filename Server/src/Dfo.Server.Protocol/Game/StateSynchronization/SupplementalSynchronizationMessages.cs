using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal abstract record SupplementalSynchronizationMessage : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    internal abstract IReadOnlyList<byte[]> EncodeFrames();
}

internal sealed class SupplementalSynchronizationEncoder<TMessage>
    : ServerMessageEncoder<TMessage>
    where TMessage : SupplementalSynchronizationMessage
{
    protected override IReadOnlyList<byte[]> EncodeMessage(TMessage message) =>
        message.EncodeFrames();
}

internal sealed record UserVisibilityMessage(
    ushort UserId,
    byte UserStateBits) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodeVisibility(UserId, UserStateBits)];
}

internal sealed record CloneTitleStateMessage(uint ItemId) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodeCloneTitle(ItemId)];
}

internal sealed record AccountRosterStateMessage(
    CharacterRosterEntryState Roster) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodeRoster(Roster)];
}

internal sealed record HonorProgressStateMessage(
    uint Level,
    uint Experience) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodeHonor(Level, Experience)];
}

internal sealed record GrowthCapsuleStateMessage(
    byte Level,
    uint TotalExperience,
    ushort Page0Sp,
    ushort Page1Sp,
    ushort Page0Tp,
    ushort Page1Tp,
    uint PvpVictoryPoints,
    uint GrowthCapsuleExperience,
    uint HonorLevel,
    uint HonorExperience) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodeGrowthCapsule(
            Level,
            TotalExperience,
            Page0Sp,
            Page1Sp,
            Page0Tp,
            Page1Tp,
            PvpVictoryPoints,
            GrowthCapsuleExperience,
            HonorLevel,
            HonorExperience)];
}

internal sealed record EntryAdmissionRejectedMessage(
    byte ErrorCode,
    string Message,
    byte NoticeMode = 0) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        SupplementalSynchronizationCodec.EncodeRejection(ErrorCode, Message, NoticeMode);
}

internal sealed record PvpLobbySnapshotMessage(
    PvpLobbySynchronizationState State) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        SupplementalSynchronizationCodec.EncodePvpLobby(State).DirectFrames;
}

internal sealed record PvpParticipantPublicationMessage(
    PvpParticipantEntryState Participant) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodePvpIdentity(Participant)];
}

internal sealed record TrailingEquipmentLockSnapshotMessage(
    Dfo.Server.Data.Inventory.CharacterInventoryEntryState CharacterInventory,
    Dfo.Server.Data.Inventory.AccountInventoryEntryState AccountInventory)
    : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodeTrailingEquipmentLocks(
            CharacterInventory, AccountInventory)];
}

internal sealed record AcceptedQuestProgressMessage(
    IReadOnlyList<AcceptedQuestProgressState> Quests)
    : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        SupplementalSynchronizationCodec.EncodeAcceptedQuests(Quests);
}

internal sealed record PetRevivalStateMessage(
    PetRevivalState State) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        SupplementalSynchronizationCodec.EncodePetRevival(State);
}

internal sealed record DungeonRejoinStateMessage(
    DungeonRejoinSynchronizationState State) : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        SupplementalSynchronizationCodec.EncodeDungeonRejoin(State);
}

internal sealed record ExpertStoreClosedMessage(ushort OwnerUserId)
    : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodeExpertStoreClose(OwnerUserId)];
}

internal sealed record TownDepartureMessage(ushort UserId)
    : SupplementalSynchronizationMessage
{
    internal override IReadOnlyList<byte[]> EncodeFrames() =>
        [SupplementalSynchronizationCodec.EncodeTownLeave(UserId)];
}

internal static class SupplementalSynchronizationEncoderRegistration
{
    public static void AddSupplementalSynchronizationEncoders(IServiceCollection services)
    {
        Add<UserVisibilityMessage>(services);
        Add<CloneTitleStateMessage>(services);
        Add<AccountRosterStateMessage>(services);
        Add<HonorProgressStateMessage>(services);
        Add<GrowthCapsuleStateMessage>(services);
        Add<EntryAdmissionRejectedMessage>(services);
        Add<PvpLobbySnapshotMessage>(services);
        Add<PvpParticipantPublicationMessage>(services);
        Add<TrailingEquipmentLockSnapshotMessage>(services);
        Add<AcceptedQuestProgressMessage>(services);
        Add<PetRevivalStateMessage>(services);
        Add<DungeonRejoinStateMessage>(services);
        Add<ExpertStoreClosedMessage>(services);
        Add<TownDepartureMessage>(services);
    }

    private static void Add<TMessage>(IServiceCollection services)
        where TMessage : SupplementalSynchronizationMessage =>
        services.AddServerMessageEncoder<TMessage, SupplementalSynchronizationEncoder<TMessage>>(
            NetworkEndpointKind.Game);
}

internal static class CleanupSynchronizationDelivery
{
    public static ExplicitMessageDelivery Create(SelectedSessionCleanupNotificationPlan plan) =>
        new(
            plan.RequiredOwnerNotices.Select(ToMessage).ToArray(),
            plan.BestEffortTargets.Select(target => new ExplicitMessageTarget(
                target.ConnectionId,
                target.OrderedNotices.Select(ToMessage).ToArray())).ToArray());

    private static IServerNetworkMessage ToMessage(SelectedSessionCleanupNotice notice) =>
        notice.Kind switch
        {
            SelectedSessionCleanupNoticeKind.ExpertStoreClosed =>
                new ExpertStoreClosedMessage(notice.UserId),
            SelectedSessionCleanupNoticeKind.TownDeparture =>
                new TownDepartureMessage(notice.UserId),
            _ => throw new InvalidDataException(
                $"Unknown selected-session cleanup notice '{notice.Kind}'."),
        };
}
