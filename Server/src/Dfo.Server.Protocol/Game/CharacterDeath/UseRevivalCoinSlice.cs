using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CharacterDeath;

public sealed record RevivalOptions(byte AckFlag);
public sealed record UseRevivalCoinRequest(GameMessageMetadata Metadata, ushort TargetActorId) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameUseRevivalCoin;
}
internal sealed class UseRevivalCoinDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 2) throw new InvalidDataException("USE_COIN requires one u16 target.");
        return new UseRevivalCoinRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body));
    }
}
internal sealed record UseRevivalCoinResponse(bool Succeeded, ushort TargetActorId, byte Flag) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameUseRevivalCoin;
}
internal sealed class UseRevivalCoinEncoder : GameCmdMessageEncoder<UseRevivalCoinResponse>
{
    internal static byte[] EncodeBody(UseRevivalCoinResponse message)
    {
        // Generic failure prefix; no target or success flag follows a rejection.
        if (!message.Succeeded) return [0, 0];
        byte[] body = [1, 0, 0, message.Flag];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), message.TargetActorId);
        return body;
    }
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UseRevivalCoinResponse message)
        => OnePayload(0x0029, EncodeBody(message));
}
public sealed record UseRevivalCoinCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Payer,
    CharacterId Target, DungeonRunId RunId) : ICommand<DungeonRevivalResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Payer.AccountId;
}
public sealed class UseRevivalCoinCommandHandler(IDungeonRevivalDomainService revival)
    : ICommandHandler<UseRevivalCoinCommand, DungeonRevivalResult>
{
    public ValueTask<DungeonRevivalResult> HandleAsync(CommandExecutionContext context,
        UseRevivalCoinCommand command, CancellationToken cancellationToken) =>
        revival.ReviveAsync(command.Payer, command.Target, command.RunId, cancellationToken);
}

public sealed class UseRevivalCoinHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry registry, DungeonProjectionGate projectionGate,
    SelectedSessionTransitionGate transitionGate, ICommandBus commands, IGameMessageSender sender,
    RevivalOptions options, TimeProvider clock, ILogger<UseRevivalCoinHandler> logger)
    : ClientMessageHandler<UseRevivalCoinRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        UseRevivalCoinRequest message, CancellationToken cancellationToken)
    {
        var payer = sessions.FindByConnection(context.Connection.ConnectionId);
        if (payer is null || runs.Find(payer.CharacterId) is not { } expected) return;
        await using var projection = await projectionGate.AcquireAsync(expected.RunId,
            UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        var frozen = registry.Find(expected.RunId);
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var account in frozen.Select(x => x.AccountId).Distinct().OrderBy(x => x.Value))
                leases.Add(await transitionGate.AcquireAsync(account, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false));
            if (!frozen.Contains(payer) || sessions.FindByCharacter(payer.CharacterId) != payer
                || sessions.FindByConnection(payer.ConnectionId) != payer) return;
            var target = frozen.SingleOrDefault(x => x.WireUserId == message.TargetActorId);
            if (target is null || sessions.FindByCharacter(target.CharacterId) != target
                || sessions.FindByConnection(target.ConnectionId) != target
                || runs.Find(target.CharacterId)?.RunId != expected.RunId
                || !onlineUsers.TryGetByConnection(payer.ConnectionId, out var online) || online is null)
            {
                await RefuseAsync(payer, message, cancellationToken).ConfigureAwait(false);
                return;
            }
            var result = await commands.SendAsync(new UseRevivalCoinCommand(online.OnlineUserId,
                payer, target.CharacterId, expected.RunId), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                await RefuseAsync(payer, message, cancellationToken).ConfigureAwait(false);
                return;
            }
            var noti = new CharacterLifeStateNotification(target.WireUserId, State: 1);
            var secondary = frozen.Where(x => x != payer
                    && sessions.FindByCharacter(x.CharacterId) == x
                    && runs.Find(x.CharacterId)?.RunId == expected.RunId)
                .Select(x => new GameExplicitMessageTarget(x.ConnectionId, [noti])).ToArray();
            // The payer receives life state before ACK; only its coin counter is updated.
            await sender.SendExplicitAsync(payer.ConnectionId, new GameExplicitMessageDelivery(
                [noti, new UseRevivalCoinResponse(true, target.WireUserId, options.AckFlag),
                 new InventoryDeltaMessage(InventoryContainerType.Main,
                    [new InventoryItemState(null, 1, result.RemainingCoin!)], clock.GetUtcNow())], secondary), cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Revival payer={Payer} target={Target} run={RunId} coins={Coins} ackFlag={Flag}",
                payer.CharacterId, target.CharacterId, expected.RunId, result.RemainingCoin!.Value, options.AckFlag);
        }
        finally
        {
            for (var i = leases.Count - 1; i >= 0; i--) await leases[i].DisposeAsync().ConfigureAwait(false);
        }
    }
    private ValueTask RefuseAsync(SelectedSessionIdentity payer, UseRevivalCoinRequest message, CancellationToken ct)
    {
        logger.LogInformation("Revival refused payer={Payer} targetActor={Target}; no coin consumed", payer.CharacterId, message.TargetActorId);
        return sender.SendCmdAsync(payer.ConnectionId, new UseRevivalCoinResponse(false, message.TargetActorId, options.AckFlag), ct);
    }
}
