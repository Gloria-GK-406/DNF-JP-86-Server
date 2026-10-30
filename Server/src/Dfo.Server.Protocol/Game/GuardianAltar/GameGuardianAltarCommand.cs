using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;

namespace Dfo.Server.Protocol.Game.GuardianAltar;

public sealed record GameGuardianAltarCommand(NetworkDispatchContext Dispatch, GuardianAltarRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GuardianAltarHandler(ICommandBus commands) : ClientMessageHandler<GuardianAltarRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameGuardianAltarCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuardianAltarRequest message, CancellationToken token) =>
        _ = await commands.SendAsync(new GameGuardianAltarCommand(context, message), token).ConfigureAwait(false);
}
internal sealed record GuardianAltarMutation(GuardianAltarProgress? Progress = null, GuardianAltarActionResult? Action = null,
    ItemDeliveryReceipt? Delivery = null);

internal sealed class GameGuardianAltarCommandHandler(IGuardianAltarDomainService altar, ISelectedSessionDomainService sessions,
    IDungeonRunQueryDomainService runs, ICommandWorkUnits units, IGameCommandOutbox outbox,
    IInventorySlotProjectionDomainService slots, ICharacterDomainService characters, TimeProvider clock)
    : ICommandHandler<GameGuardianAltarCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuardianAltarCommand command, CancellationToken ct)
    {
        if (GameRequestFreshness.IsStale(command.Input.Metadata)) return true;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || !sessions.IsInWorld(session)) return true;
        var input = command.Input;
        var result = await units.WriteAsync(context, new("guardian-altar.action",
            [new(session, command.Dispatch.Connection, input.Metadata)]), (_, token) => MutateAsync(session, input, token), ct).ConfigureAwait(false);
        if (!sessions.IsInWorld(session)) return true;
        var frames = new List<IGameServerMessage>();
        if (input.Operation == GuardianAltarOperation.Open)
            frames.Add(new GuardianAltarOpenResponse(result.Progress is { } opened ? altar.DescribeProgress(opened) : null));
        else
        {
            frames.Add(new GuardianAltarActionResponse(input, result.Progress is not null || result.Action is not null ? 0 : 2,
                result.Progress, result.Action?.State ?? (runs.Find(session.CharacterId) is { } run ? altar.Find(run.RunId) : null)));
            if (input.Operation == GuardianAltarOperation.Reset && result.Progress is { } reset)
            {
                frames.Add(new GuardianAltarProfileMessage(altar.DescribeProgress(reset)));
                frames.Add(GameInventoryDelivery.CarriedGoldNotification(await characters.Get(session.CharacterId).GoldAsync(ct).ConfigureAwait(false), clock.GetUtcNow()));
            }
        }
        frames.AddRange(GuardianAltarProjection.InventoryFrames(result.Delivery, slots, clock));
        await outbox.RegisterExplicitAsync(session.ConnectionId, new(frames, []), ct).ConfigureAwait(false);
        if (result.Action is { } action) await GuardianAltarProjection.PublishAsync(action, altar, sessions, runs, outbox, slots, clock, ct).ConfigureAwait(false);
        return true;
    }
    private async ValueTask<GuardianAltarMutation> MutateAsync(SelectedSessionIdentity session, GuardianAltarRequest r, CancellationToken ct)
    {
        if (r.Operation is GuardianAltarOperation.Buy or GuardianAltarOperation.SetSlot or GuardianAltarOperation.Exchange && r.RidableId != 1) return new();
        switch (r.Operation)
        {
            case GuardianAltarOperation.Open: return new(await altar.OpenAsync(session, ct).ConfigureAwait(false));
            case GuardianAltarOperation.Buy when r.Kind is >= 0 and <= 2:
                return new(await altar.BuyAsync(session, (GuardianAltarShopKind)r.Kind, r.ItemId, r.FirstLevel, r.LastLevel, ct).ConfigureAwait(false));
            case GuardianAltarOperation.SetSlot when r.Kind is 1 or 2 && r.Slot < 6:
                return new(await altar.SetSlotAsync(session, checked((byte)(r.Slot + (r.Kind == 2 ? 6 : 0))),
                    r.Kind == 1 ? GuardianAltarShopKind.Unit : GuardianAltarShopKind.Skill, r.Attach ? r.ItemId : 0, ct).ConfigureAwait(false));
            case GuardianAltarOperation.Exchange when r.Kind is 1 or 2 && r.Kind == r.SecondKind && r.Slot < 6 && r.SecondSlot < 6:
                return new(await altar.ExchangeSlotsAsync(session, checked((byte)(r.Slot + (r.Kind == 2 ? 6 : 0))),
                    checked((byte)(r.SecondSlot + (r.Kind == 2 ? 6 : 0))), r.ItemId, r.SecondItemId, ct).ConfigureAwait(false));
            case GuardianAltarOperation.UpgradeEnergy:
                return new(Action: await altar.UpgradeEnergyAsync(session, r.EnergyLevel, ct).ConfigureAwait(false));
            case GuardianAltarOperation.Summon:
                return new(Action: await altar.SummonAsync(session, r.Slot, r.ItemId, r.EnergyLevel, ct).ConfigureAwait(false));
            case GuardianAltarOperation.Pause:
                return new(Action: await altar.PauseAsync(session, r.Attach, ct).ConfigureAwait(false));
            case GuardianAltarOperation.Achievement:
                var claim = await altar.ClaimAchievementAsync(session, r.ItemId, ct).ConfigureAwait(false);
                return new(claim?.Progress, Delivery: claim?.Delivery);
            case GuardianAltarOperation.Reset when r.ItemId == 0:
                return new(await altar.ResetStarsAsync(session, ct).ConfigureAwait(false));
            default: return new();
        }
    }
}
