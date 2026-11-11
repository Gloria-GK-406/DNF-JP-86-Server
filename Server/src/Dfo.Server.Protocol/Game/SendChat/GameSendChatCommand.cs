using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SendChat;

public sealed record GameSendChatCommand(NetworkDispatchContext Dispatch, SendChatRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>The one entry every chat line takes: wire mode to scope, scope to recipients, one line to each.</summary>
/// <remarks>
/// A line that went through, or that reached no one, is not answered: the A21 client does not wait
/// for an answer to a chat line. Nor does it show its own line when sending, so every scope but a
/// whisper includes the speaker. A whisper reaches only its target, as on the 70 server.
/// </remarks>
internal sealed class GameSendChatCommandHandler(
    ISelectedSessionDomainService sessions,
    IChatScopeDomainService scopes,
    ICharacterDomainService characters,
    IMailboxQueryDomainService directory,
    IChannelEntryDomainService channels,
    IMaterialConsumptionDomainService materials,
    ICommandWorkUnits units,
    ICommandProjections projections,
    IGameCommandOutbox outbox,
    TimeProvider clock,
    ILogger<GameSendChatCommandHandler> logger) : ICommandHandler<GameSendChatCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSendChatCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var request = command.Input;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } speaker || !sessions.IsInWorld(speaker))
            return false;
        if (ChatWireModes.Route(request.Mode) is not { } route)
            return await RefuseAsync(speaker, request, "UnsupportedMode", ct).ConfigureAwait(false);
        // Refused rather than cut: a cut line is not what the player wrote.
        if (request.Text.Length == 0 || request.Text.Length > route.TextLimit)
            return await RefuseAsync(speaker, request, "TextLength", ct).ConfigureAwait(false);
        if (!PassesPreSendChecks(speaker, route))
            return await RefuseAsync(speaker, request, "PreSendCheck", ct).ConfigureAwait(false);

        var recipient = new SelectedSessionRecipient(speaker, connection, request.Metadata);
        var facts = route.NameLimit is null && route.Kind != ChatKind.Whisper ? new ChatFacts(null, null)
            : await units.ReadOnlyAsync(execution, new("ChatParticipants", [recipient]),
                token => ReadFactsAsync(speaker, route, request, token), ct).ConfigureAwait(false);
        if (route.NameLimit is { } nameLimit && (facts.SpeakerName is not { Length: > 0 } name || name.Length > nameLimit))
            return await RefuseAsync(speaker, request, "SpeakerName", ct).ConfigureAwait(false);

        byte channelId = 0;
        if (route.MegaphoneItemId is not null)
        {
            var channel = await channels.ResolveAsync(speaker.ListenerPort, ct).ConfigureAwait(false);
            if (channel.ChannelId is < 0 or > byte.MaxValue)
                return await RefuseAsync(speaker, request, "ChannelId", ct).ConfigureAwait(false);
            channelId = (byte)channel.ChannelId;
        }

        IReadOnlyList<IGameServerMessage> followUp = [];
        if (route.MegaphoneItemId is { } itemId)
        {
            if (MegaphoneSlot(request.Aux) is not { } slot)
                return await RefuseAsync(speaker, request, "UnmappedSlot", ct).ConfigureAwait(false);
            var consumed = await units.WriteAsync(execution, new("ChatMegaphone", [recipient]),
                (_, token) => materials.ConsumeAsync(speaker, [new MaterialCost(slot, itemId, 1)], token), ct).ConfigureAwait(false);
            if (!consumed.Success)
                return await RefuseAsync(speaker, request, consumed.Rejection!, ct).ConfigureAwait(false);
            followUp = [SlotState(consumed.Debits.Single())];
        }

        var members = Scope(speaker, route.Kind, facts.Target) is { } scope ? scopes.MembersOf(scope) : [];
        if (route.Kind == ChatKind.Whisper)
            members = [.. members.Where(member => member != speaker.CharacterId)];
        if (members.Count != 0)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(members),
                new ChatMessagePosted(Line(speaker, route.Kind, request, facts.SpeakerName, channelId), speaker.CharacterId, followUp), ct)
                .ConfigureAwait(false);
        else if (followUp.Count != 0)
            await outbox.RegisterExplicitAsync(connection.ConnectionId, new(followUp, []), ct).ConfigureAwait(false);
        logger.LogDebug("Chat character={Character} mode={Mode} kind={Kind} recipients={Recipients}",
            speaker.CharacterId, request.Mode, route.Kind, members.Count);
        return true;
    }

    /// <summary>
    /// The single check a line passes before it is delivered. Every line passes today; muting,
    /// block lists and rate limits belong here, and nowhere else.
    /// </summary>
    private static bool PassesPreSendChecks(SelectedSessionIdentity speaker, ChatRoute route) => true;

    private async ValueTask<ChatFacts> ReadFactsAsync(SelectedSessionIdentity speaker, ChatRoute route,
        SendChatRequest request, CancellationToken ct)
    {
        var name = route.NameLimit is null ? null
            : (await characters.Get(speaker.CharacterId).IdentityAsync(ct).ConfigureAwait(false))?.Name;
        CharacterId? target = null;
        if (route.Kind == ChatKind.Whisper && request.TargetName is { Length: > 0 } targetName)
            target = (await directory.FindRecipientAsync(targetName, ct).ConfigureAwait(false))?.CharacterId;
        return new ChatFacts(name, target);
    }

    // A whisper to no one by that name, or to someone offline, is silent: the speaker is told nothing.
    private static ChatScope? Scope(SelectedSessionIdentity speaker, ChatKind kind, CharacterId? target) => kind switch
    {
        ChatKind.Area => new ChatScope.AreaOf(speaker.CharacterId),
        ChatKind.Party => new ChatScope.PartyOf(speaker.CharacterId),
        ChatKind.Guild => new ChatScope.GuildOf(speaker.CharacterId),
        ChatKind.ChannelMegaphone => new ChatScope.ChannelOf(speaker.CharacterId),
        ChatKind.ServerMegaphone => new ChatScope.Server(),
        ChatKind.Whisper => target is { } character ? new ChatScope.Character(character) : null,
        _ => throw new NotSupportedException($"Chat kind '{kind}' has no scope."),
    };

    private static IGameNotiMessage Line(SelectedSessionIdentity speaker, ChatKind kind, SendChatRequest request,
        byte[]? name, byte channelId) => kind switch
    {
        ChatKind.Area or ChatKind.Party or ChatKind.Guild => new ChatLineMessage(request.Mode, speaker.WireUserId, request.Text),
        ChatKind.Whisper => new WhisperMessage(request.Mode, name!, request.Text),
        ChatKind.ChannelMegaphone or ChatKind.ServerMegaphone => new MegaphoneMessage(request.Mode, GameProtocol.ServerIndex,
            channelId, speaker.WireUserId, name!, request.Text),
        _ => throw new NotSupportedException($"Chat kind '{kind}' has no frame."),
    };

    private static BusinessSlot? MegaphoneSlot(uint aux)
    {
        if (aux > ushort.MaxValue) return null;
        try { return A21InventoryLayout.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, checked((int)aux))); }
        catch (InventorySlotMappingException) { return null; }
    }

    private IGameServerMessage SlotState(MaterialDebit debit)
    {
        var slot = A21InventoryLayout.ToGameSlot(debit.Cost.Slot);
        var at = clock.GetUtcNow();
        return debit.Remaining is { } remaining
            ? GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(slot.Space,
                [new InventoryItemState(null, slot.Index, remaining)], at))
            : GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, at);
    }

    private async ValueTask<bool> RefuseAsync(SelectedSessionIdentity speaker, SendChatRequest request, string reason, CancellationToken ct)
    {
        logger.LogInformation("Chat refused character={Character} mode={Mode} bytes={Bytes} reason={Reason}",
            speaker.CharacterId, request.Mode, request.Text.Length, reason);
        await outbox.RegisterCmdAsync(speaker.ConnectionId, new SendChatRefusal(), ct).ConfigureAwait(false);
        return false;
    }

    private sealed record ChatFacts(byte[]? SpeakerName, CharacterId? Target);
}

public sealed record GameItemLinkChatCommand(NetworkDispatchContext Dispatch, ItemLinkChatRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Item-link lines are not delivered yet; the refusal keeps the client's input from waiting.</summary>
internal sealed class GameItemLinkChatCommandHandler(
    ISelectedSessionDomainService sessions,
    IGameCommandOutbox outbox,
    ILogger<GameItemLinkChatCommandHandler> logger) : ICommandHandler<GameItemLinkChatCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameItemLinkChatCommand command, CancellationToken ct)
    {
        if (sessions.FindByConnection(command.Dispatch.Connection.ConnectionId) is not { } speaker || !sessions.IsInWorld(speaker))
            return false;
        logger.LogInformation("Item-link chat refused character={Character}", speaker.CharacterId);
        await outbox.RegisterCmdAsync(speaker.ConnectionId, new ItemLinkChatRefusal(), ct).ConfigureAwait(false);
        return false;
    }
}

public sealed class SendChatHandler(ICommandBus commands) : ClientMessageHandler<SendChatRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameSendChatCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SendChatRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSendChatCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class ItemLinkChatHandler(ICommandBus commands) : ClientMessageHandler<ItemLinkChatRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameItemLinkChatCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ItemLinkChatRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameItemLinkChatCommand(context, message), ct).ConfigureAwait(false);
}
