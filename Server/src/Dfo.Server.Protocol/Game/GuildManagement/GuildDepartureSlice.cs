using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildApplications;
using Dfo.Server.Protocol.Game.CharacterBuffs;
using Dfo.Server.Protocol.Game.GuildCreation;
using Dfo.Server.Protocol.Game.GuildDungeon;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.GuildMemberInfo;
using Dfo.Server.Protocol.Game.GuildRoster;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildManagement;

public sealed record GuildDepartureRequest(ClientMessageType MessageType, GameMessageMetadata Metadata,
    bool Valid, byte[]? Name = null) : IClientNetworkMessage;

internal sealed class GuildSecedeDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var name = GuildCreationPayloadCodec.DecodeText(body, 0, 62);
        return new GuildDepartureRequest(ClientMessageType.GameGuildSecede, metadata, name is not null,
            name is null ? null : GuildCreationPayloadCodec.EncodeGuildText(name));
    }
}

internal sealed class GuildDisbandDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildDepartureRequest(ClientMessageType.GameGuildDisband, metadata, body.IsEmpty);
}

internal enum GuildDepartureReason : byte
{
    Left = 1,
    Kicked = 2,
    Disbanded = 3,
}

internal sealed record GuildSecedeReply(string Guild, byte[] Member, GuildDepartureReason Reason) : IGameCmdMessage
{
    public ServerMessageType MessageType => (ServerMessageType)ClientMessageType.GameGuildSecede;
}

internal sealed class GuildSecedeReplyEncoder : GameCmdMessageEncoder<GuildSecedeReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildSecedeReply message)
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write((byte)message.Reason);
        ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(message.Guild));
        ChatDString.Write(writer, message.Member);
        return OnePayload(message, GameCmdEnvelope.Accept(body.ToArray()));
    }
}

internal sealed record GuildDepartureMessage(GuildDepartureReason Reason) : IGameNotiMessage
{
    public ServerMessageType MessageType => Reason == GuildDepartureReason.Disbanded
        ? ServerMessageType.GameGuildDismissed : ServerMessageType.GameGuildSeceded;
}

internal sealed class GuildDepartureEncoder : GameNotiMessageEncoder<GuildDepartureMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildDepartureMessage message) =>
        // A21 ignores this optional byte. The compatibility patch uses it to select the kick text.
        OnePayload(message, message.Reason == GuildDepartureReason.Kicked
            ? new byte[] { (byte)message.Reason } : Array.Empty<byte>());
}

internal sealed record GuildMembershipEnded(GuildDepartureReason Reason, IReadOnlyList<GuildAttributeBonus> Bonuses)
    : INotification<GuildMembershipEnded>;

internal sealed class GuildMembershipEndedHandler(IGuildMembershipQueryDomainService guilds) : INotificationHandler<GuildMembershipEnded>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildMembershipEnded message, CancellationToken ct)
    {
        if (await guilds.FindMembershipAsync(recipient.CharacterId, ct).ConfigureAwait(false) is not null)
            return [];
        return
        [
            new GuildDepartureMessage(message.Reason), GuildMemberInfoMessage.From(null),
            new GuildDungeonInfoMessage(null),
            .. message.Bonuses.Select(bonus => new CharacterBuffRemoved(bonus.BuffId)),
        ];
    }
}

public sealed record GameGuildDepartureCommand(NetworkDispatchContext Dispatch, GuildDepartureRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameGuildDepartureCommandHandler(
    ISelectedSessionDomainService sessions, IGuildMembershipDomainService guilds, IGuildMembershipQueryDomainService query,
    IGuildAllianceDomainService alliances, IGuildBenefitDomainService benefits, IGuildOverviewDomainService overviews,
    ICharacterVisibleSessionDomainService visibility, IChannelEntryDomainService channels,
    ICommandWorkUnits units, IGameCommandOutbox outbox, ICommandProjections projections)
    : ICommandHandler<GameGuildDepartureCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameGuildDepartureCommand command, CancellationToken ct)
    {
        var input = command.Input;
        if (GameRequestFreshness.IsStale(input.Metadata)) return false;
        var connection = command.Dispatch.Connection;
        var disband = input.MessageType == ClientMessageType.GameGuildDisband;
        var result = input.Valid && sessions.TryGetInWorld(connection.ConnectionId, out var actor)
            ? await units.WriteAsync(execution, new("guild-departure", [new(actor, connection, input.Metadata)]),
                (_, token) => DepartAsync(actor.CharacterId, input.Name, disband, token), ct).ConfigureAwait(false)
            : Departure.Refused;

        await outbox.RegisterCmdAsync(connection.ConnectionId, result.Reply
            ?? new GuildCreationReply((ServerMessageType)(int)input.MessageType, 0x22), ct).ConfigureAwait(false);
        if (result.Reply is null)
        {
            if (result.FailureNotice is { } text)
                await outbox.RegisterNotiAsync(connection.ConnectionId, new GameServerNoticeMessage(text), ct).ConfigureAwait(false);
            return false;
        }
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(result.Departed),
            new GuildMembershipEnded(result.Reason, result.Bonuses), ct).ConfigureAwait(false);
        foreach (var character in result.Departed)
            if (sessions.TryGetInWorld(character, out var online))
                await projections.ProjectAsync(new NotificationAudience.TheseSessions(visibility.Resolve(online)),
                    new CharacterUserStateRefresh(online, RefreshGuildMedalAura: true), ct).ConfigureAwait(false);
        foreach (var applicant in result.Applicants)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([applicant]),
                new GuildApplicationResolved(applicant, null), ct).ConfigureAwait(false);
        if (result.FormerAlly is { Guild: not null } ally)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(
                ally.Members.Select(member => member.Character).ToArray()), ally, ct).ConfigureAwait(false);
        if (result.Remaining.Count != 0)
        {
            var audience = new NotificationAudience.TheseCharacters(result.Remaining.Select(row => row.Membership.Character).ToArray());
            await projections.ProjectAsync(audience, result.Panel, ct).ConfigureAwait(false);
            if (await GuildRosterProjection.BuildAsync(result.Remaining, sessions, channels, ct).ConfigureAwait(false) is { } roster)
                await projections.ProjectAsync(audience, new GuildRosterChanged(roster), ct).ConfigureAwait(false);
        }
        return true;
    }

    private async ValueTask<Departure> DepartAsync(CharacterId actor, byte[]? targetName, bool disband, CancellationToken ct)
    {
        if (await query.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Guild: var guild })
            return Departure.Refused;
        var roster = await query.ReadRosterEntriesAsync(actor, ct).ConfigureAwait(false);
        var bonuses = await benefits.ReadAttributeBonusesAsync(guild.Id, ct).ConfigureAwait(false);
        if (disband)
        {
            var ally = await alliances.FindAllyAsync(guild.Id, ct).ConfigureAwait(false);
            var result = await guilds.DisbandAsync(actor, ct).ConfigureAwait(false);
            if (result.Status != GuildMutationStatus.Success)
                return Departure.Refused with
                {
                    FailureNotice = result.Status switch
                    {
                        GuildMutationStatus.WarehouseNotEmpty => "请先清空公会仓库，再解散公会。",
                        GuildMutationStatus.GuildHasOtherMembers => "公会只剩会长时才能解散。",
                        _ => null,
                    },
                };
            var departed = roster.Select(row => row.Membership.Character).ToArray();
            var formerAlly = ally is null ? GuildInfoPanel.Empty
                : await GuildInfoPanel.ReadAsync(overviews, ally.Leader, ct).ConfigureAwait(false);
            return new Departure(new GuildCreationReply((ServerMessageType)ClientMessageType.GameGuildDisband),
                GuildDepartureReason.Disbanded, departed, [.. (result.AffectedCharacters ?? []).Except(departed)],
                bonuses, GuildInfoPanel.Empty, [], FormerAlly: formerAlly);
        }

        var target = roster.FirstOrDefault(row => targetName is not { Length: > 0 }
            ? row.Membership.Character == actor : row.Name.AsSpan().SequenceEqual(targetName));
        if (target is null)
            return Departure.Refused;
        var dismissed = target.Membership.Character;
        var change = dismissed == actor ? await guilds.LeaveAsync(actor, ct).ConfigureAwait(false)
            : await guilds.RemoveMemberAsync(actor, dismissed, ct).ConfigureAwait(false);
        if (change.Status != GuildMutationStatus.Success)
            return Departure.Refused;

        var remaining = roster.Where(row => row.Membership.Character != dismissed).ToArray();
        var panel = remaining.Length == 0 ? GuildInfoPanel.Empty
            : await GuildInfoPanel.ReadAsync(overviews, remaining[0].Membership.Character, ct).ConfigureAwait(false);
        var reason = dismissed == actor ? GuildDepartureReason.Left : GuildDepartureReason.Kicked;
        return new Departure(new GuildSecedeReply(guild.Name, target.Name, reason), reason,
            [dismissed], [], bonuses, panel, remaining);
    }

    private sealed record Departure(IGameCmdMessage? Reply, GuildDepartureReason Reason,
        IReadOnlyList<CharacterId> Departed, IReadOnlyList<CharacterId> Applicants, IReadOnlyList<GuildAttributeBonus> Bonuses,
        GuildInfoPanel Panel, IReadOnlyList<GuildRosterEntry> Remaining, string? FailureNotice = null, GuildInfoPanel? FormerAlly = null)
    {
        public static Departure Refused { get; } = new(null, default, [], [], [], GuildInfoPanel.Empty, []);
    }
}

public sealed class GuildDepartureHandler(ICommandBus commands)
    : ClientMessageHandler<GuildDepartureRequest>, IProtocolCommandMessageHandler<GameGuildDepartureCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildDepartureRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildDepartureCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildDepartureRegistration
{
    internal static void AddGuildDepartureSlice(IServiceCollection services)
    {
        const ClientMessageType secede = ClientMessageType.GameGuildSecede;
        const ClientMessageType disband = ClientMessageType.GameGuildDisband;
        services.AddGameCmdMessageDecoder<GuildSecedeDecoder>(secede);
        services.AddGameCmdMessageDecoder<GuildDisbandDecoder>(disband);
        services.AddGameCmdMessageEncoder<GuildSecedeReply, GuildSecedeReplyEncoder>();
        services.AddGameNotiMessageEncoder<GuildDepartureMessage, GuildDepartureEncoder>();
        services.AddNotificationHandler<GuildMembershipEnded, GuildMembershipEndedHandler>();
        services.AddProtocolCommandHandler<GameGuildDepartureCommand, bool, GameGuildDepartureCommandHandler>(
            new ProtocolCommandMessageBinding(secede, typeof(GuildDepartureRequest)),
            new ProtocolCommandMessageBinding(disband, typeof(GuildDepartureRequest)));
        services.RegisterProtocolMessageHandler<GuildDepartureHandler, GuildDepartureRequest, GameGuildDepartureCommand>(secede);
        services.RegisterProtocolMessageHandler<GuildDepartureHandler, GuildDepartureRequest, GameGuildDepartureCommand>(disband);
    }
}
