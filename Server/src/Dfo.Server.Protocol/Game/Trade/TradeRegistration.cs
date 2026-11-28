using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Application.Trade;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Trade;

public sealed record SetTradeStateRequest(GameMessageMetadata Metadata, byte State, bool Valid) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetTradeState;
}

internal sealed class SetTradeStateDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length is 1 or 4 or 8 && body[0] <= 5 && body[1..].IndexOfAnyExcept((byte)0) < 0
            ? new SetTradeStateRequest(metadata, body[0], true) : new SetTradeStateRequest(metadata, 0, false);
}

public sealed record GameSetTradeStateCommand(NetworkDispatchContext Dispatch, SetTradeStateRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class SetTradeStateHandler(ICommandBus commands)
    : ClientMessageHandler<SetTradeStateRequest>, IProtocolCommandMessageHandler<GameSetTradeStateCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SetTradeStateRequest message, CancellationToken ct) =>
        await commands.SendAsync(new GameSetTradeStateCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed class GameSetTradeStateCommandHandler(TradeProtocol protocol) : ICommandHandler<GameSetTradeStateCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, GameSetTradeStateCommand command, CancellationToken ct) =>
        command.Input.Valid
            ? protocol.AdvanceAsync(context, command.Dispatch.Connection, command.Input.Metadata, command.Input.State, ct)
            : ValueTask.FromResult(false);
}

internal sealed class TradeExpiryTask(TradeProtocol protocol, PlayerTradeDomainService trades) : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution,
        ScheduledTaskContext scheduled, CancellationToken ct)
    {
        foreach (var trade in trades.InvalidTrades())
            await protocol.CloseAsync(trade, false, ct).ConfigureAwait(false);
        return ScheduledTaskBusinessResult.Success;
    }
}

internal static class TradeRegistration
{
    internal static void AddPlayerTradeSlice(this IServiceCollection services)
    {
        services.AddSingleton<TradeProtocol>();
        services.AddGameCmdMessageDecoder<SetTradeStateDecoder>(ClientMessageType.GameSetTradeState);
        services.AddGameCmdMessageEncoder<TradePeerReply, TradePeerReplyEncoder>();
        services.AddGameNotiMessageEncoder<TradeNotice, TradeNoticeEncoder>();
        services.AddProtocolCommandHandler<GameSetTradeStateCommand, bool, GameSetTradeStateCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetTradeState, typeof(SetTradeStateRequest)));
        services.RegisterProtocolMessageHandler<SetTradeStateHandler, SetTradeStateRequest, GameSetTradeStateCommand>(
            ClientMessageType.GameSetTradeState, typeof(SetTradeStateRequest));
        services.AddScoped<TradeExpiryTask>();
        services.AddScheduledTask(new("player-trade.expiry", new IntervalTaskSchedule(TimeSpan.FromSeconds(1)),
            typeof(TradeExpiryTask)));
    }
}
