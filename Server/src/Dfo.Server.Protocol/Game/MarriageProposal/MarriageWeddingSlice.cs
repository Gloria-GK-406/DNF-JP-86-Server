using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

internal sealed record WeddingAvatarNotification(MarriageChurchSnapshot Church, bool Restore = false) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingAvatar; }
internal sealed class WeddingAvatarEncoder : GameNotiMessageEncoder<WeddingAvatarNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingAvatarNotification m)
    {
        var b = new byte[67]; b[0] = m.Restore ? (byte)1 : (byte)0;
        MarriageChurchPayload.CopyName(m.Church.ApplicantName, b.AsSpan(2, 30));
        MarriageChurchPayload.CopyName(m.Church.PartnerName, b.AsSpan(32, 30));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(62), m.Church.Room.AreaId);
        return OnePayload(0x03CF, b);
    }
}
internal sealed record WeddingBeginNotification(MarriageChurchSnapshot Church) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingBegin; }
internal sealed record WeddingLetterNotification(MarriageChurchSnapshot Church, uint Key, bool Paid = false) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingLetter; }
internal sealed class WeddingBeginEncoder : GameNotiMessageEncoder<WeddingBeginNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingBeginNotification m)
    {
        var b = new byte[68];
        BinaryPrimitives.WriteUInt32LittleEndian(b, checked((uint)m.Church.Peer.Value));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), m.Church.Room.AreaId);
        MarriageChurchPayload.CopyName(m.Church.ApplicantName, b.AsSpan(8, 30));
        MarriageChurchPayload.CopyName(m.Church.PartnerName, b.AsSpan(38, 30));
        return OnePayload(0x03D7, b);
    }
}
internal sealed class WeddingLetterEncoder : GameNotiMessageEncoder<WeddingLetterNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLetterNotification m)
    {
        var b = new byte[78];
        MarriageChurchPayload.Preparation(m.Church).AsSpan(0, 77).CopyTo(b);
        BinaryPrimitives.WriteUInt32LittleEndian(b, m.Key); b[77] = m.Paid ? (byte)1 : (byte)0;
        return OnePayload(0x03D5, b);
    }
}
internal sealed record WeddingInvitedGuestsNotification(IReadOnlyList<uint> Guests) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingInvitedGuests; }
internal sealed class WeddingInvitedGuestsEncoder : GameNotiMessageEncoder<WeddingInvitedGuestsNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingInvitedGuestsNotification m)
    {
        var b = new byte[1 + 4 * m.Guests.Count]; b[0] = checked((byte)m.Guests.Count);
        for (var i = 0; i < m.Guests.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1 + 4 * i), m.Guests[i]);
        return OnePayload(0x03D6, b);
    }
}
public sealed record WeddingLetterRequest(GameMessageMetadata Metadata, byte Mode, uint Target, ushort OnlineId, byte Kind) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameWeddingSendLetter; }
public sealed record WeddingGiftRequest(GameMessageMetadata Metadata, uint Key, int Gold, string Text) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameWeddingMoneyGift; }
internal sealed class WeddingLetterDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (b.Length != 22) throw new InvalidDataException("Wedding letter requires 22 body bytes.");
        return new WeddingLetterRequest(m, b[14], BinaryPrimitives.ReadUInt32LittleEndian(b[15..]), BinaryPrimitives.ReadUInt16LittleEndian(b[19..]), b[21]);
    }
}
internal sealed class WeddingGiftDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (b.Length < 12) throw new InvalidDataException("Wedding gift is truncated.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(b[8..]);
        if (length < 0 || length > 1024 || b.Length != 12 + length) throw new InvalidDataException("Invalid blessing length.");
        return new WeddingGiftRequest(m, BinaryPrimitives.ReadUInt32LittleEndian(b), BinaryPrimitives.ReadInt32LittleEndian(b[4..]), new UTF8Encoding(false, true).GetString(b[12..]));
    }
}
internal sealed record WeddingLetterAck(int Status, ushort Remaining) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingSendLetter; }
internal sealed record WeddingGiftAck(int Status, uint Key) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameWeddingMoneyGift; }
internal sealed class WeddingLetterAckEncoder : GameCmdMessageEncoder<WeddingLetterAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingLetterAck m)
    {
        var b = new byte[m.Status == 0 ? 7 : 5]; b[0] = 1; BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), m.Status);
        if (m.Status == 0) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(5), m.Remaining);
        return OnePayload(0x03F6, b);
    }
}
internal sealed class WeddingGiftAckEncoder : GameCmdMessageEncoder<WeddingGiftAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingGiftAck m)
    {
        var b = new byte[m.Status == 0 ? 9 : 5]; b[0] = 1; BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), m.Status);
        if (m.Status == 0) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5), m.Key);
        return OnePayload(0x03F2, b);
    }
}
public sealed record AdvanceMarriageWeddingCommand(Guid Ceremony, IReadOnlyList<AccountId> AccountIds)
    : ICommand<MarriageWeddingAdvanceResult>, IMultiAccountCommand;
public sealed class AdvanceMarriageWeddingCommandHandler(IMarriageVenueDomainService venue)
    : ICommandHandler<AdvanceMarriageWeddingCommand, MarriageWeddingAdvanceResult>
{
    public async ValueTask<MarriageWeddingAdvanceResult> HandleAsync(CommandExecutionContext c, AdvanceMarriageWeddingCommand q, CancellationToken ct) => new(await venue.AdvanceAsync(q.Ceremony, ct).ConfigureAwait(false));
}
public sealed record CompleteWeddingCleanupCommand(SelectedSessionIdentity Actor, Guid Ceremony) : ICommand<bool>, IAccountCommand, ISelectedSessionCommand
{ public AccountId AccountId => Actor.AccountId; public SelectedSessionIdentity? Session => Actor; }
public sealed class CompleteWeddingCleanupCommandHandler(IMarriageVenueDomainService venue) : ICommandHandler<CompleteWeddingCleanupCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext c, CompleteWeddingCleanupCommand q, CancellationToken ct)
    { await venue.CompleteCleanupAsync(q.Ceremony, q.Actor.CharacterId, ct).ConfigureAwait(false); return true; }
}
public sealed record WeddingLetterCommand(SelectedSessionIdentity Actor, byte Mode, uint Target) : ICommand<MarriageWeddingLetterResult>, IAccountCommand, ISelectedSessionCommand
{ public AccountId AccountId => Actor.AccountId; public SelectedSessionIdentity? Session => Actor; }
public sealed class WeddingLetterCommandHandler(IMarriageVenueDomainService venue) : ICommandHandler<WeddingLetterCommand, MarriageWeddingLetterResult>
{
    public ValueTask<MarriageWeddingLetterResult> HandleAsync(CommandExecutionContext c, WeddingLetterCommand q, CancellationToken ct) => venue.InviteAsync(q.Actor, q.Mode, q.Target, ct);
}
public sealed record WeddingGiftCommand(SelectedSessionIdentity Actor, uint Key, int Gold) : ICommand<int>, IAccountCommand, ISelectedSessionCommand
{ public AccountId AccountId => Actor.AccountId; public SelectedSessionIdentity? Session => Actor; }
public sealed class WeddingGiftCommandHandler(IMarriageVenueDomainService venue) : ICommandHandler<WeddingGiftCommand, int>
{
    public ValueTask<int> HandleAsync(CommandExecutionContext c, WeddingGiftCommand q, CancellationToken ct) => venue.GiftAsync(q.Actor, q.Key, q.Gold, ct);
}
internal sealed class WeddingLetterHandler(ISelectedSessionDomainService sessions, ICommandBus commands, IGameMessageSender sender, SelectedSessionTransitionGate gate)
    : ClientMessageHandler<WeddingLetterRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, WeddingLetterRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        await WeddingProposalExecution.RunAsync(gate, actor, actor, async () =>
        {
            var result = actor is null ? new MarriageWeddingLetterResult(-1, 0, [])
                : await commands.SendAsync(new WeddingLetterCommand(actor, m.Mode, m.Target), ct).ConfigureAwait(false);
            var messages = new List<IGameServerMessage> { new WeddingLetterAck(result.Status, result.Remaining) };
            if (result.Status == 0) messages.Add(new WeddingInvitedGuestsNotification(result.Invited));
            await sender.SendExplicitAsync(c.Connection.ConnectionId, new(messages,
                result.Letters.Select(x => new GameExplicitMessageTarget(x.Guest.ConnectionId, [new WeddingLetterNotification(x.Church, x.Key)])).ToArray()), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
internal sealed class WeddingGiftHandler(ISelectedSessionDomainService sessions, ICommandBus commands, IGameMessageSender sender,
    SelectedSessionTransitionGate gate, ICharacterDomainService characters, TimeProvider clock) : ClientMessageHandler<WeddingGiftRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, WeddingGiftRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        await WeddingProposalExecution.RunAsync(gate, actor, actor, async () =>
        {
            var result = actor is null ? -1 : await commands.SendAsync(new WeddingGiftCommand(actor, m.Key, m.Gold), ct).ConfigureAwait(false);
            var messages = new List<IGameServerMessage> { new WeddingGiftAck(result, m.Key) };
            if (result == 0 && actor is not null) messages.Add(GameInventoryDelivery.CarriedGoldNotification(await characters.Get(actor.CharacterId).GoldAsync(ct), clock.GetUtcNow()));
            await sender.SendExplicitAsync(c.Connection.ConnectionId, new(messages, []), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
public sealed class MarriageWeddingProjection(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService views,
    IOnlineCharacterRuntimeDomainService onlineCharacters, ITownAreaDomainService areas, TownAreaProjectionDelivery projection, IGameMessageSender sender)
{
    public ValueTask AppearanceAsync(SelectedSessionIdentity actor, MarriageChurchSnapshot church, bool restore, CancellationToken ct) =>
        sender.SendExplicitAsync(actor.ConnectionId, new([new WeddingAvatarNotification(church, restore)], []), ct);
    public async ValueTask PositionAsync(MarriageWeddingStanding standing, CancellationToken ct)
    {
        var actor = standing.Actor;
        if (sessions.Find(actor.AccountId) != actor || areas.Find(actor.CharacterId) is not { } area || !actor.Owns(area)
            || area.Area.TownId != standing.Position.TownId || area.Area.AreaId != standing.Position.AreaId) return;
        var moved = onlineCharacters.StepTo(actor, standing.Position.X, standing.Position.Y, standing.Position.Direction);
        await projection.ForcePositionAsync(actor, moved, ct).ConfigureAwait(false);
    }
    public async ValueTask<bool> MoveAsync(SelectedSessionIdentity actor, int channel, MarriageChurchLocation location, CancellationToken ct)
    {
        if (sessions.Find(actor.AccountId) != actor || areas.Find(actor.CharacterId) is not { } previous || !actor.Owns(previous)
            || await views.TryLoadAsync(actor, ct).ConfigureAwait(false) is not { } view) return false;
        var moved = onlineCharacters.CrossInto(actor, location.TownId, location.AreaId, location.X, location.Y);
        return await projection.TransferAsync(actor.ConnectionId, actor, view, channel, moved, previous, true, ct).ConfigureAwait(false);
    }
}
internal sealed class MarriageWeddingScheduler(IMarriageQueryDomainService marriage, ICommandBus commands,
    SelectedSessionTransitionGate gate, IGameMessageSender sender,
    MarriageWeddingProjection projection, ILogger<MarriageWeddingScheduler> logger, TimeProvider clock) : BackgroundService
{
    private async ValueTask ProjectAsync(Func<ValueTask> action, CancellationToken ct)
    {
        try { await action().ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { logger.LogWarning(e, "Committed wedding projection failed for one recipient; other recipients continue."); }
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            foreach (var ceremony in await marriage.ActiveWeddingsAsync(ct).ConfigureAwait(false))
            {
                var accounts = await marriage.WeddingAccountsAsync(ceremony, ct).ConfigureAwait(false);
                var leases = new List<IAsyncDisposable>();
                try
                {
                    foreach (var account in accounts) leases.Add(await gate.AcquireAsync(account, UserAccessMode.Exclusive, ct).ConfigureAwait(false));
                    if (!(await marriage.WeddingAccountsAsync(ceremony, ct).ConfigureAwait(false)).SequenceEqual(accounts)) continue;
                    var result = (await commands.SendAsync(new AdvanceMarriageWeddingCommand(ceremony, accounts), ct).ConfigureAwait(false)).Transition;
                    if (result is null) continue;
                    logger.LogInformation("Wedding {Ceremony} reached {Phase}", ceremony, result.Wedding.Phase);
                    var completed = result.Wedding.Phase == MarriageWeddingPhase.Completed;
                    // Broadcast both authoritative positions before either endpoint starts the cinema.
                    foreach (var standing in result.Standing)
                        await ProjectAsync(() => projection.PositionAsync(standing, ct), ct).ConfigureAwait(false);
                    foreach (var participant in result.Participants)
                    {
                        var messages = new List<IGameServerMessage>();
                        if (completed) messages.Add(new WeddingBeginNotification(participant.Church));
                        messages.Add(new WeddingInfoMessage(await marriage.SnapshotAsync(participant.Actor.AccountId, participant.Actor.CharacterId, ct).ConfigureAwait(false)));
                        if (!completed) messages.Add(new GameServerNoticeMessage("婚礼因双方未能按时到场而取消，费用已通过邮件退回。"));
                        await ProjectAsync(() => sender.SendExplicitAsync(participant.Actor.ConnectionId, new(messages, []), ct), ct).ConfigureAwait(false);
                    }
                    foreach (var guest in result.Guests)
                        if (completed) await ProjectAsync(() => sender.SendNotiAsync(guest.Actor.ConnectionId, new WeddingBeginNotification(guest.Church), ct), ct).ConfigureAwait(false);
                    // Move first: op1 may itself cause a native 0024 if the client is still in church.
                    foreach (var departure in result.Departures)
                        await ProjectAsync(async () => { await projection.MoveAsync(departure.Actor, departure.Channel, departure.Location, ct).ConfigureAwait(false); }, ct).ConfigureAwait(false);
                    foreach (var cleanup in result.Cleanup)
                        await ProjectAsync(async () =>
                        {
                            await projection.AppearanceAsync(cleanup.Actor, cleanup.Church, true, ct).ConfigureAwait(false);
                            await commands.SendAsync(new CompleteWeddingCleanupCommand(cleanup.Actor, ceremony), ct).ConfigureAwait(false);
                        }, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e) { logger.LogError(e, "Wedding advancement failed for {Ceremony}", ceremony); }
                finally { for (var n = leases.Count - 1; n >= 0; n--) await leases[n].DisposeAsync().ConfigureAwait(false); }
            }
        }
    }
}
