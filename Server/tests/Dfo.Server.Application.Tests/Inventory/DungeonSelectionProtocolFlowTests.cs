using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
namespace Dfo.Server.Application.Tests.Inventory;
public sealed class DungeonSelectionProtocolFlowTests
{
    [Fact]
    public async Task Task42_real_selection_and_return_are_one_shot_and_keep_the_gate_position()
    {
        using var f=Fixture();await Wire(f,0xf,[0,0]);
        Assert.Contains(f.Sender.Messages,m=>m.MessageType==ServerMessageType.GameEnterSelectDungeonAcknowledgement);
        TestGameplay.Run(f.Services,()=>{Assert.NotNull(f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session));Assert.Null(f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId));});
        f.Sender.Messages.Clear();await Wire(f,0x84,[]);
        Assert.Equal(new[]{ServerMessageType.GameBackToVillage,ServerMessageType.GameUserState,ServerMessageType.GameUserArea,ServerMessageType.GameAreaUsers,ServerMessageType.GameWeaknessProjection},f.Sender.Messages.Select(m=>m.MessageType));
        TestGameplay.Run(f.Services,()=>{Assert.Null(f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session));Assert.Equal(new Dfo.Server.Data.Characters.CharacterPositionState(0,0,12,34,5),f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Position);});
        f.Sender.Messages.Clear();await Wire(f,0x84,[]);Assert.IsType<BackToVillageResponse>(Assert.Single(f.Sender.Messages));
    }
    [Fact]
    public async Task Task42_selection_actual_encoder_failure_keeps_the_recorded_intent_and_town_departure()
    {
        using var f=Fixture();var cause=new IOException("task42 selection actual encoder");f.Sender.Outbound.Encoder.Fail((_,_)=>cause);
        var failure=await Record.ExceptionAsync(()=>Wire(f,0xf,[0,0]).AsTask());Assert.Same(cause,failure);
        Assert.Empty(f.Sender.Messages);
        TestGameplay.Run(f.Services,()=>{Assert.Equal(DungeonEntryIntent.Interactive,f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session));Assert.Null(f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId));});
    }
    [Fact]
    public async Task Guild_selection_captures_the_gate_before_departure_and_a_new_open_drops_the_old_origin()
    {
        var guild = new Guild(new(12), "guild", "", 1);
        using var f = Fixture(s => s.AddSingleton(TestRequired.Absent<IGuildMembershipQueryDomainService>((method, args) =>
            method.Name == nameof(IGuildMembershipQueryDomainService.FindMembershipAsync)
                ? ValueTask.FromResult<GuildMembership?>(new(guild,
                    new(guild.Id, (Dfo.Server.Data.Characters.CharacterId)args![0]!, true, DateTimeOffset.UnixEpoch)
                        { MembershipId = 543 }))
                : null)));
        var runtime = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
        var intents = f.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
        var gate = new TownAreaKey(1, GuildHallTown.TownId, GuildHallTown.DungeonGateAreaId, 12);
        TestGameplay.Run(f.Services, () => runtime.CrossInto(f.Session, gate, 10, 10, true));
        await Wire(f, 0xF, [0, 0]);
        var selection = Assert.IsType<GuildDungeonSelection>(intents.Find(f.Session)?.GuildSelection);
        Assert.Equal(new GuildDungeonGateOrigin(gate, 543), selection.Origins[f.Session]);
        Assert.Null(runtime.StandingOf(f.Session));

        await Wire(f, 0xF, [0, 0]);
        Assert.Null(intents.Find(f.Session)?.GuildSelection);
        await Wire(f, 0x84, []);
        Assert.Null(intents.Find(f.Session));
    }

    private static ShopFixture Fixture(Action<IServiceCollection>? configure = null)=>TeleportConsumableTests.Fixture(s=>
    {configure?.Invoke(s);s.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p=>new Views(p));s.AddSingleton<ICharacterStateProjectionDomainService>(TownMovementProtocolCommandFlowTests.Proxy<ICharacterStateProjectionDomainService>.Create((_,args)=>ValueTask.FromResult((SelectCharacterResult)args![0]!)));s.AddSingleton<IDungeonVisitDomainService>(TownMovementProtocolCommandFlowTests.Proxy<IDungeonVisitDomainService>.Create((name,_)=>name=="DefersSelectionState"?true:throw new NotSupportedException(name)));});
    private sealed class Views(IServiceProvider p):ISelectedCharacterCurrentViewDomainService
    {public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session,CancellationToken ct){var baseline=await Characters.CharacterEntryInventoryTests.CreateLifecycleResultAsync(session.AccountId,session.CharacterId.Value,0);var inventory=await p.GetRequiredService<ICharacterInventoryEntryDomainService>().ReadCurrentAsync(session.AccountId,session.CharacterId,ct);return baseline with{CharacterInventory=inventory,CharacterEntry=baseline.CharacterEntry with{Skills=baseline.CharacterEntry.Skills with{ProtocolMirror=new(0,0,0,0)},Public=baseline.CharacterEntry.Public with{Identity=new(0,0,0)}}};}}
    private static ValueTask Wire(ShopFixture f,ushort type,byte[] body)
    {var metadata=new GameMessageMetadata(1,type,0,7){InboundOrdinal=7,SequenceIsFresh=true}.WithRawBody(body);var d=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==type);var message=((IGameMessageDecoder)f.Services.GetRequiredService(d.DecoderType)).Decode(metadata,body)!;return f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),message,default);}
}
