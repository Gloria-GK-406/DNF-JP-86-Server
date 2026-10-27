
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

// A local composition harness. Fixture.RunAsync owns the actual existing transaction;
// this does not exercise the production bus/request runner or replace the real-wire evidence.
internal static class MoveItemProtocolTestDriver
{
    internal sealed record Execution(IInventoryMoveDomainService Domain, MoveItemFixture Fixture);
    internal static MoveItemSpaceHandler Create(ISelectedSessionDomainService sessions,
        ISelectedCharacterCurrentViewDomainService views,IInventorySlotMappingRuleService slots,
        Execution execution,IGameMessageSender sender,ILogger<GameMoveItemSpaceCommandHandler> logger,
        ICharacterInventoryEntryDomainService character,IAccountInventoryEntryDomainService account,
        TimeProvider time,IMarriageHousingQueryDomainService? housing=null) =>
        new(new Bus(new(sessions,views,slots,execution.Domain,new Units(execution.Fixture),
            new Outbox(sender),logger,character,account,time,housing ?? ProtocolAlgorithmFixture.Default<IMarriageHousingQueryDomainService>())));
    private sealed class Bus(GameMoveItemSpaceCommandHandler handler):ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public async ValueTask<T> SendAsync<T>(ICommand<T> command,CancellationToken ct=default)where T:notnull
        {
            var move=Assert.IsType<GameMoveItemSpaceCommand>(command);
            return (T)(object)await handler.HandleAsync(DomainCommandFixture.Context(Guid.NewGuid(),typeof(GameMoveItemSpaceCommand),
                typeof(GameMoveItemSpaceCommandHandler),FixtureUnitKind.Direct),move,ct);
        }
    }
    private sealed class Units(MoveItemFixture fixture):ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,
            Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken ct=default)where T:notnull
        {Assert.Equal(CommandWorkUnitKind.Ordinary,d.Kind);Assert.Equal(CommandFootprintMode.None,d.Footprint);Assert.Empty(d.Participants);return fixture.RunAsync(()=>action(null!,ct));}
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c,CommandWorkUnitDeclaration d,Func<CancellationToken,ValueTask<T>> a,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
    }
    private sealed class Outbox(IGameMessageSender sender):IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage m,CancellationToken ct)=>sender.SendCmdAsync(id,m,ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage m,CancellationToken ct)=>sender.SendNotiAsync(id,m,ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext target,IReadOnlyList<IGameServerMessage> m,CancellationToken ct)=>sender.SendBatchAsync(target,m,ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct)=>sender.SendExplicitAsync(id,d,ct);
    }
}
