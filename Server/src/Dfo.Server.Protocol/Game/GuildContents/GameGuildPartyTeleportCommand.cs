using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.Party;

namespace Dfo.Server.Protocol.Game.GuildContents;

public enum PartyTeleportSource : byte
{
    Unknown = 0,
    Aura = 1,
    Guild = 2,
}

public sealed record GuildPartyTeleportRequest(
    GameMessageMetadata Metadata, GuildPartyTeleportTarget? Target, PartyTeleportSource Source)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildPartyTeleport;
}

internal sealed class GuildPartyTeleportDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 8
            ? new GuildPartyTeleportRequest(metadata,
                new(body[0], body[1], BinaryPrimitives.ReadInt16LittleEndian(body[2..]),
                    BinaryPrimitives.ReadInt16LittleEndian(body[4..]), body[6]),
                (PartyTeleportSource)body[7])
            : new GuildPartyTeleportRequest(metadata, null, PartyTeleportSource.Unknown);
}

internal sealed record GuildPartyTeleportReply(bool Accepted, byte TownId) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildPartyTeleport;
}

internal sealed class GuildPartyTeleportReplyEncoder : GameCmdMessageEncoder<GuildPartyTeleportReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildPartyTeleportReply message) =>
        OnePayload(message, message.Accepted ? GameCmdEnvelope.Accept([])
            // The failure branch reads a town byte, a dword and a byte before dispatching the error.
            : [0, 0x15, message.TownId, 0, 0, 0, 0, 0]);
}

public sealed class GuildPartyTeleportRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildPartyTeleportRequest>, IProtocolCommandMessageHandler<GameGuildPartyTeleportCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildPartyTeleportRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildPartyTeleportCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record GameGuildPartyTeleportCommand(NetworkDispatchContext Dispatch, GuildPartyTeleportRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildPartyTeleportCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildPartyTeleportDomainService planner,
    IGuildBenefitDomainService benefits, IGuildOverviewDomainService overviews,
    ICharacterLocationDomainService locations,
    IOnlineCharacterRuntimeDomainService online,
    ITownTransitDomainService transit,
    ITownAreaDomainService areas,
    ISelectedCharacterCurrentViewDomainService views,
    IMarriageHousingQueryDomainService housing,
    IPartyService parties,
    IPartyProjectionStateDomainService partyState,
    ICommandWorkUnits units,
    ICommandProjections projections,
    IGameCommandOutbox outbox) : ICommandHandler<GameGuildPartyTeleportCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildPartyTeleportCommand command, CancellationToken ct)
    {
        var request = command.Request;
        if (GameRequestFreshness.IsStale(request.Metadata))
            return false;
        var connection = command.Dispatch.Connection;
        if (request.Source == PartyTeleportSource.Aura)
            return await RefuseAsync("光环组队传送暂未开放。").ConfigureAwait(false);
        if (request.Source != PartyTeleportSource.Guild)
            return await RefuseAsync("无法发起组队传送。").ConfigureAwait(false);
        if (request.Target is not { } target || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await RefuseAsync("无法发起公会组队传送。").ConfigureAwait(false);

        var prepared = await planner.PrepareAsync(actor, target, ct).ConfigureAwait(false);
        if (prepared.Plan is not { } plan)
            return await RefuseAsync(prepared.Refusal switch
            {
                GuildPartyTeleportRefusal.PartyLeaderRequired => "请由队长发起公会组队传送。",
                GuildPartyTeleportRefusal.PartyNotTogether => "请让所有队员到同一城镇区域集合。",
                GuildPartyTeleportRefusal.DestinationLocked => "存在无法前往该地区的队员。",
                GuildPartyTeleportRefusal.AlreadyThere => "队伍已在目标位置。",
                _ => "该地点无法使用公会组队传送。",
            }).ConfigureAwait(false);

        var priorViews = new List<(TownAreaMember Member, SelectCharacterResult View)>(plan.Members.Count);
        foreach (var member in plan.Members)
        {
            if (member.Area == plan.Destination && member.Position == plan.Position)
                continue;
            if (await views.TryLoadAsync(member.Session, ct).ConfigureAwait(false) is not { } current)
                return await RefuseAsync("队员状态已变化，请重新发起传送。").ConfigureAwait(false);
            priorViews.Add((member, current));
        }

        var participants = plan.Members.Select(member => member.Session == actor
            ? new SelectedSessionRecipient(actor, connection, request.Metadata)
            : new SelectedSessionRecipient(member.Session, null)).ToArray();
        var settlement = await units.WriteAsync(context, new("guild-party-teleport", participants), async (_, token) =>
        {
            if (plan.Members.Any(member => online.StandingOf(member.Session) != member))
                return new Settlement(GuildMutationStatus.CharacterUnavailable, GuildInfoPanel.Empty);

            var consumed = await benefits.ConsumeTeleportAsync(actor.CharacterId, token).ConfigureAwait(false);
            if (consumed.Status != GuildMutationStatus.Success)
                return new Settlement(consumed.Status, GuildInfoPanel.Empty);

            foreach (var (member, _) in priorViews)
                await locations.PersistAsync(member.Session.AccountId, member.CharacterId,
                    transit.StoredPositionAfter(member.Position, plan.Position), token).ConfigureAwait(false);

            var panel = await GuildInfoPanel.ReadAsync(overviews, actor.CharacterId, token).ConfigureAwait(false);
            return new Settlement(consumed.Status, panel);
        }, ct).ConfigureAwait(false);
        if (settlement.Status != GuildMutationStatus.Success)
            return await RefuseAsync(settlement.Status == GuildMutationStatus.CharacterUnavailable
                ? "队员状态已变化，请重新发起传送。"
                : "公会组队传送剩余次数不足。").ConfigureAwait(false);

        var moved = new List<(TownAreaMember Member, SelectCharacterResult View)>(priorViews.Count);
        foreach (var (member, view) in priorViews)
            if (transit.Cross(member.Session, plan.Destination, plan.Position.X, plan.Position.Y,
                    sharesTownPresence: true, direction: plan.Position.Direction) is not null)
                moved.Add((member, view));

        await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildPartyTeleportReply(true, target.TownId), ct)
            .ConfigureAwait(false);
        var partyTown = new PartyTownProjectionDelivery(parties, areas, sessions, projections, partyState);
        foreach (var (member, view) in moved)
            await TownMovementProjection.TownProjectTransferAsync(sessions, areas, projections, partyTown, views,
                housing, outbox, member.Session.ConnectionId, member.Session, view, member, ct).ConfigureAwait(false);

        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(
            settlement.Panel.Members.Select(member => member.Character).ToArray()), settlement.Panel, ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> RefuseAsync(string reason)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId,
                new GuildPartyTeleportReply(false, request.Target?.TownId ?? 0), ct).ConfigureAwait(false);
            await outbox.RegisterNotiAsync(connection.ConnectionId, new GameServerNoticeMessage(reason), ct).ConfigureAwait(false);
            return false;
        }
    }

    private sealed record Settlement(GuildMutationStatus Status, GuildInfoPanel Panel);
}
