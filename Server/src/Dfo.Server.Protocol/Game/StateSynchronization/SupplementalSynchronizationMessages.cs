using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal abstract record SupplementalSynchronizationMessage : IGameServerMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;
    internal abstract IReadOnlyList<GameMessagePayload> EncodePayloads();
}

internal sealed class SupplementalCmdSynchronizationEncoder<TMessage>
    : GameCmdMessageEncoder<TMessage>
    where TMessage : SupplementalSynchronizationMessage, IGameCmdMessage
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TMessage message) =>
        message.EncodePayloads();
}

internal sealed class SupplementalNotiSynchronizationEncoder<TMessage>
    : GameNotiMessageEncoder<TMessage>
    where TMessage : SupplementalSynchronizationMessage, IGameNotiMessage
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TMessage message) =>
        message.EncodePayloads();
}

internal sealed record UserVisibilityMessage(
    ushort UserId,
    byte UserStateBits) : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodeVisibility(UserId, UserStateBits)];
}

internal sealed record CloneTitleStateMessage(uint ItemId)
    : SupplementalSynchronizationMessage, IGameCmdMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodeCloneTitle(ItemId)];
}

internal sealed record AccountRosterStateMessage(
    CharacterRosterEntryState Roster) : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodeRoster(Roster)];
}

internal sealed record HonorProgressStateMessage(
    uint Level,
    uint Experience) : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
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
    uint HonorExperience) : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
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

internal sealed record EntryAdmissionRejectedCommandMessage(byte ErrorCode)
    : SupplementalSynchronizationMessage, IGameCmdMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodeRejectionCommand(ErrorCode)];
}

internal sealed record EntryAdmissionNoticeMessage(
    string Message,
    byte NoticeMode = 0) : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodeRejectionNotice(Message, NoticeMode)];
}

internal sealed record PvpLobbySnapshotMessage(
    PvpLobbySynchronizationState State) : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        SupplementalSynchronizationCodec.EncodePvpLobby(State).DirectPayloads;
}

internal sealed record PvpParticipantPublicationMessage(
    PvpParticipantEntryState Participant) : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodePvpIdentity(Participant)];
}

internal sealed record TrailingEquipmentLockSnapshotMessage(
    Dfo.Server.Data.Inventory.CharacterInventoryEntryState CharacterInventory,
    Dfo.Server.Data.Inventory.AccountInventoryEntryState AccountInventory)
    : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodeTrailingEquipmentLocks(
            CharacterInventory, AccountInventory)];
}

internal sealed record AcceptedQuestProgressMessage(
    IReadOnlyList<AcceptedQuestProgressState> Quests)
    : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        SupplementalSynchronizationCodec.EncodeAcceptedQuests(Quests);
}

internal sealed record PetRevivalStateMessage(
    PetRevivalState State) : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        SupplementalSynchronizationCodec.EncodePetRevival(State);
}

internal sealed record ExpertStoreClosedMessage(ushort OwnerUserId)
    : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodeExpertStoreClose(OwnerUserId)];
}

internal sealed record TownDepartureMessage(ushort UserId)
    : SupplementalSynchronizationMessage, IGameNotiMessage
{
    internal override IReadOnlyList<GameMessagePayload> EncodePayloads() =>
        [SupplementalSynchronizationCodec.EncodeTownLeave(UserId)];
}

internal static class SupplementalSynchronizationEncoderRegistration
{
    public static void AddSupplementalSynchronizationEncoders(IServiceCollection services)
    {
        AddNoti<UserVisibilityMessage>(services);
        AddCmd<CloneTitleStateMessage>(services);
        AddNoti<AccountRosterStateMessage>(services);
        AddNoti<HonorProgressStateMessage>(services);
        AddNoti<GrowthCapsuleStateMessage>(services);
        AddCmd<EntryAdmissionRejectedCommandMessage>(services);
        AddNoti<EntryAdmissionNoticeMessage>(services);
        AddNoti<PvpLobbySnapshotMessage>(services);
        AddNoti<PvpParticipantPublicationMessage>(services);
        AddNoti<TrailingEquipmentLockSnapshotMessage>(services);
        AddNoti<AcceptedQuestProgressMessage>(services);
        AddNoti<PetRevivalStateMessage>(services);
        AddNoti<ExpertStoreClosedMessage>(services);
        AddNoti<TownDepartureMessage>(services);
    }

    private static void AddCmd<TMessage>(IServiceCollection services)
        where TMessage : SupplementalSynchronizationMessage, IGameCmdMessage =>
        services.AddGameCmdMessageEncoder<TMessage, SupplementalCmdSynchronizationEncoder<TMessage>>();

    private static void AddNoti<TMessage>(IServiceCollection services)
        where TMessage : SupplementalSynchronizationMessage, IGameNotiMessage =>
        services.AddGameNotiMessageEncoder<TMessage, SupplementalNotiSynchronizationEncoder<TMessage>>();
}

internal static class CleanupSynchronizationDelivery
{
    public static GameExplicitMessageDelivery Create(SelectedSessionCleanupNotificationPlan plan) =>
        new(
            plan.RequiredOwnerNotices.Select(ToMessage).ToArray(),
            plan.BestEffortTargets.Select(target => new GameExplicitMessageTarget(
                target.ConnectionId,
                target.OrderedNotices.Select(ToMessage).ToArray())).ToArray());

    private static IGameNotiMessage ToMessage(SelectedSessionCleanupNotice notice) =>
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
