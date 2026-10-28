using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class TownPeerProtocolCommandFlowTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_crossing_and_teleport_reconcile_old_area_publish_to_new_neighbors_and_refresh_party(bool teleport)
    {
        using var f = TeleportConsumableTests.Fixture(services =>
        {
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>();
            services.AddSingleton<ITownDefinitionSource>(new Towns(teleport));
            services.AddSingleton<ITownMovementDomainService>(TownMovementProtocolCommandFlowTests.Proxy<ITownMovementDomainService>.Create((_, _) => ValueTask.FromResult(true)));
        });
        var channel = await f.Services.GetRequiredService<IChannelEntryDomainService>().ResolveAsync(f.Connection.ListenerPort, default);
        SelectedSessionIdentity oldNeighbor = null!, newNeighbor = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            oldNeighbor = await TowerSourceMembers.AddAsync(f, 3, 2, 2, 4000);
            newNeighbor = await TowerSourceMembers.AddAsync(f, 4, 3, 2, 4000);
            var runtime = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
            var towns = f.Services.GetRequiredService<ITownAreaDomainService>();
            towns.TransferExact(f.Session, new(channel.ChannelId, 0, 0),
                runtime.PositionOf(f.Session)!, true);
            foreach (var (session, town, area) in new[] { (oldNeighbor, (byte)0, (byte)0), (newNeighbor, (byte)11, (byte)3) })
            {
                var position = new CharacterPositionState(town, area, 100, 200, 0);
                runtime.Begin(session, position, 100);
                towns.Enter(new(session, new(channel.ChannelId, town, area), position, true));
            }
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Member(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId, session.WireUserId, new(channel.ChannelId, 0, 0));
            Assert.True(parties.RequestInvite(Member(f.Session), Member(oldNeighbor), new(0, 100, 100, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Member(oldNeighbor), Member(f.Session)).Succeeded);
        });
        if (teleport) { await f.Seed(65, 2000, 1); await TeleportConsumableTests.Send(f); }
        else await EffectRequestTransport.SendRaw(f, new(1, 0x24, 0, 7), [11, 3, 123, 0, 234, 0]);
        var oldMessages = f.Sender.Outbound.MessagesFor(oldNeighbor.ConnectionId);
        Assert.Contains(oldMessages, m => m.MessageType == ServerMessageType.GameAreaUsers);
        var newMessages = f.Sender.Outbound.MessagesFor(newNeighbor.ConnectionId);
        if (teleport)
        {
            Assert.DoesNotContain(newMessages, m => m.GetType().Name == "CurrentUserStateMessage");
            Assert.Contains(oldMessages, m => m.MessageType == ServerMessageType.GamePartyRoster);
        }
        else
        {
            Assert.Contains(newMessages, m => m.GetType().Name == "CurrentUserStateMessage");
            Assert.Contains(newMessages, m => m.MessageType == ServerMessageType.GamePartyRoster);
        }
        Assert.DoesNotContain(newMessages, m => m is Dfo.Server.Protocol.Game.Teleport.TeleportResponse);
        TestGameplay.Run(f.Services, () =>
        {
            var party = f.Services.GetRequiredService<IPartyService>().FindParty(new(f.Session.AccountId, f.Session.CharacterId, f.Session.WireUserId, null));
            Assert.Equal(2, party.Members.Length);
            if (teleport) Assert.Null(party.LeaderMember.Character.DirectoryScope);
            else Assert.Equal(new PartyDirectoryScope(channel.ChannelId, 11, 3), party.LeaderMember.Character.DirectoryScope);
            Assert.Equal((byte)11, f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Area.TownId);
        });
        Assert.Equal(11, f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
        if (teleport) Assert.Equal(0, f.Count(2000));
        Assert.Empty(f.Sender.Outbound.Closes);
    }
    private sealed class Towns(bool teleport) : ITownDefinitionSource
    {
        public ValueTask<TownDefinition?> GetAsync(int town, CancellationToken ct) => ValueTask.FromResult<TownDefinition?>(new(town, "town", [new(3, "normal.map", teleport ? TownAreaKind.Gate : TownAreaKind.Normal, 474, 234) { MinimumLevel = 2 }]));
        public ValueTask<IReadOnlyList<TownDefinition>> GetAllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TownDefinition>>([]);
        public ValueTask<MapGeometry?> GetMapGeometryAsync(string map, CancellationToken ct) => ValueTask.FromResult<MapGeometry?>(null);
    }
}
