using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Support;

// Direct mechanism fixture: explicit owner and request, never an ambient request or a production wire claim.
internal sealed class EffectFixtureContext
{
    internal EffectOperationEntry Entry { get; }
    internal ExplicitEffectRequest State { get; }
    internal CommandExecutionContext Context { get; }
    internal CommandRequestContext.ProtocolEffectSelected Request { get; }
    internal bool Authorized = true;
    internal EffectFixtureContext(ShopFixture f, ReplayProjectionNeeds minimum = ReplayProjectionNeeds.None)
    {
        Entry = new(f.Session, 1, 1, 3, 1, new string('A',64), 1);
        State = new(Entry, new(f.Session,f.Connection), minimum, f.Services.GetRequiredService<SelectedSessionSafety>());
        var message = new Input(GameMessageMetadata.Empty);
        Request = new(new(f.Connection), message, new ProtocolFrameMetadata.Game(message.Metadata), () => Authorized, State);
        Context = new(Guid.NewGuid(),typeof(Input),typeof(EffectFixtureContext)) {Request=Request};
    }
    private sealed record Input(GameMessageMetadata Metadata) : IEffectSettlementRequest
    { public ClientMessageType MessageType=>ClientMessageType.GameExit; public ReplayProjectionNeeds MinimumReplayNeeds=>ReplayProjectionNeeds.None; }
}
