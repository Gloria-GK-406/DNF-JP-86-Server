using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class ExpiredItemsProjectionTests
{
    [Fact]
    public async Task Expired_worn_item_refreshes_owner_and_visible_peer_without_readding_equipment()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        source = source with { CharacterInventory = source.CharacterInventory with
        {
            Containers = [.. source.CharacterInventory.Containers.Where(x => x.ContainerType != InventoryContainerType.Equipment),
                new(InventoryContainerType.Equipment, 0, [])]
        }};
        var sessions = Dfo.Server.Testing.TestSessions.Directory();
        var owner = SelectedSessionIdentity.Create(source.AccountInventory.AccountId, source.Character.Identity.CharacterId, new("owner"), 1, 10011);
        var peer = SelectedSessionIdentity.Create(new(90), new(91), new("peer"), 1, 10011);
        sessions.Select(owner); sessions.Select(peer);
        var towns = new TownAreaDomainService();
        foreach (var session in new[] { owner, peer })
            towns.Enter(new TownAreaMember(session.CharacterId, session.TownAreaLease(), new(1, 1, 2), new(1, 2, 1, 1, 0), true));
        var visibility = new CharacterVisibleSessionDomainService(sessions, towns, new DungeonRunDomainService());
        var reads = new ReadModel(source);
        var handler = new ExpiredItemsRefreshHandler(sessions, reads, visibility, reads, reads);
        var notice = new ExpiredItemsRefresh(owner, [InventoryContainerType.Equipment], DateTimeOffset.UtcNow);
        var own = await handler.ComposeAsync(new(owner.CharacterId, owner.ConnectionId, owner), notice, default);
        Assert.Empty(Assert.Single(own.OfType<ExtendedUserStateMessage>()).Equipment);
        Assert.Empty(Assert.Single(own.OfType<CurrentUserStateMessage>()).Equipment);
        Assert.DoesNotContain(own, x => x is InventoryFullSnapshotMessage);
        var others = await handler.ComposeAsync(new(peer.CharacterId, peer.ConnectionId, peer), notice, default);
        Assert.Empty(Assert.IsType<CurrentUserStateMessage>(Assert.Single(others)).Equipment);
        sessions.Select(owner with { ConnectionGeneration = 2 });
        Assert.Empty(await handler.ComposeAsync(new(owner.CharacterId, owner.ConnectionId, owner), notice, default));
        Assert.Empty(await handler.ComposeAsync(new(peer.CharacterId, peer.ConnectionId, peer), notice, default));
    }

    private sealed class ReadModel(SelectCharacterResult source) : ISelectedCharacterCurrentViewDomainService,
        ICharacterInventoryEntryDomainService, IAccountInventoryEntryDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken ct) => ValueTask.FromResult<SelectCharacterResult?>(source);
        public ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(AccountId account, CharacterId character, CancellationToken ct) => ValueTask.FromResult(source.CharacterInventory);
        public ValueTask<CharacterInventoryEntryState> LoadAsync(AccountId account, CharacterId character, CancellationToken ct) => ValueTask.FromResult(source.CharacterInventory);
        ValueTask<AccountInventoryEntryState> IAccountInventoryEntryDomainService.LoadAsync(AccountId account, CharacterId character, CancellationToken ct) => ValueTask.FromResult(source.AccountInventory);
    }
}
