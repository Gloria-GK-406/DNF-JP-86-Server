using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.CharacterDeath;
internal sealed class GameUseRevivalCoinCommandHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, IDungeonRunDomainService runs, IDungeonRunProjectionDomainService registry,
    IDungeonRevivalDomainService revival, ICommandWorkUnits units, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger<GameUseRevivalCoinCommandHandler> logger, IBossTowerDomainService bossTowers,
    IIllusionTowerDomainService illusionTowers, IDungeonParticipationDomainService participation)
    : ICommandHandler<GameUseRevivalCoinCommand,bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameUseRevivalCoinCommand command, CancellationToken token)
    {await RunAsync(execution,command.Dispatch,command.Input,token).ConfigureAwait(false);return true;}
    internal ValueTask<DungeonRevivalResult> ReviveAsync(CommandExecutionContext execution,SelectedSessionIdentity payer,CharacterId target,DungeonRunId run,CancellationToken token) =>
        units.WriteAsync(execution,new("use-revival-coin",[]),(unit,ct)=>revival.ReviveAsync(payer,target,run,ct),token);
    private async ValueTask RunAsync(CommandExecutionContext execution, NetworkDispatchContext context,
        UseRevivalCoinRequest message, CancellationToken cancellationToken)
    {
        if (GameRequestFreshness.IsStale(message.Metadata)) return;
        var payer = sessions.FindByConnection(context.Connection.ConnectionId);
        if (payer is null) return;
        switch (DungeonLocator.Locate(payer, participation, bossTowers, illusionTowers))
        {
            // A Boss tower has no revival: its channel is the refusal alone. An illusion tower
            // seat has no ordinary run, so the ordinary flow ends silently there.
            case DungeonPlace.InBossTower:
                await RefuseAsync(payer, message, cancellationToken).ConfigureAwait(false);
                return;
            default:
                await ReviveOrdinaryAsync(execution, payer, message, cancellationToken).ConfigureAwait(false);
                return;
        }
    }
    private async ValueTask ReviveOrdinaryAsync(CommandExecutionContext execution, SelectedSessionIdentity payer,
        UseRevivalCoinRequest message, CancellationToken cancellationToken)
    {
        if (runs.Find(payer.CharacterId) is not { } expected) return;
        var frozen = registry.Find(expected.RunId);
        if (!frozen.Contains(payer) || !sessions.IsInWorld(payer)) return;
        var target = frozen.SingleOrDefault(x => x.WireUserId == message.TargetActorId);
        if (target is null || !sessions.IsInWorld(target)
            || runs.Find(target.CharacterId)?.RunId != expected.RunId
            || !onlineUsers.TryGetByConnection(payer.ConnectionId, out var online) || online is null)
        {
            await RefuseAsync(payer, message, cancellationToken).ConfigureAwait(false);
            return;
        }
        var result = await ReviveAsync(execution, payer, target.CharacterId, expected.RunId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            await RefuseAsync(payer, message, cancellationToken).ConfigureAwait(false);
            return;
        }
        var noti = new CharacterLifeStateNotification(target.WireUserId, State: 1);
        var secondary = frozen.Where(x => x != payer
                && sessions.IsInWorld(x)
                && runs.Find(x.CharacterId)?.RunId == expected.RunId)
            .Select(x => new GameExplicitMessageTarget(x.ConnectionId, [noti])).ToArray();
        // The payer receives life state before ACK; only its coin counter is updated.
        await outbox.RegisterExplicitAsync(payer.ConnectionId, new GameExplicitMessageDelivery(
            [noti, new UseRevivalCoinResponse(true, target.WireUserId, message.AckFlag),
             new InventoryDeltaMessage(InventoryContainerType.Main,
                [new InventoryItemState(null, 1, result.RemainingCoin!)], clock.GetUtcNow())], secondary), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Revival payer={Payer} target={Target} run={RunId} coins={Coins} ackFlag={Flag}",
            payer.CharacterId, target.CharacterId, expected.RunId, result.RemainingCoin!.Value, message.AckFlag);
    }
    private ValueTask RefuseAsync(SelectedSessionIdentity payer, UseRevivalCoinRequest message, CancellationToken ct)
    {
        logger.LogInformation("Revival refused payer={Payer} targetActor={Target}; no coin consumed", payer.CharacterId, message.TargetActorId);
        return outbox.RegisterCmdAsync(payer.ConnectionId, new UseRevivalCoinResponse(false, message.TargetActorId, message.AckFlag), ct);
    }
}
