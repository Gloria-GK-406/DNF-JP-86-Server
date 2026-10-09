using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class RememberedTownTests
{
    [Dfo.Server.Testing.PvfTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Selection_reads_saved_town_and_resolves_its_private_gate_instead_of_old_coordinates(byte town)
    {
        using var shop = new ShopFixture();
        var saved = new CharacterPositionState(town, 2, 123, 456, 3);
        await shop.Services.GetRequiredService<ISelectedSessionPositionPersistence>()
            .PersistAsync(shop.Session.AccountId, shop.Session.CharacterId, saved, default);
        // Reads through the actual repository, not the in-memory selected-session position.
        var loaded = await shop.Services.GetRequiredService<ICharacterDomainService>()
            .LoadByRosterPositionAsync(shop.Session.AccountId, 0, default);
        Assert.Equal(saved, loaded.Position);
        Assert.Equal(town, loaded.LastInnTownId);
        var gate = (await shop.Services.GetRequiredService<ITownDefinitionSource>().GetAsync(town, default))!.Gate!;
        var placement = await shop.Services.GetRequiredService<IEntryPlacementRuleService>()
            .ResolveAsync(new(10, 10011, ChannelEntryKind.General), loaded.LastInnTownId, default);
        Assert.Equal(town, placement.Spawn.TownId);
        Assert.Equal(gate.AreaId, placement.Spawn.AreaId);
        Assert.Equal(gate.Operand1, placement.Spawn.X);
        Assert.Equal(gate.Operand2, placement.Spawn.Y);
        Assert.False(placement.Spawn.SharesTownPresence);
        var other = await shop.Services.GetRequiredService<ICharacterDomainService>()
            .LoadByRosterPositionAsync(shop.Session.AccountId, 1, default);
        Assert.Equal(0, other.Position.TownId);
        Assert.Equal(0, other.LastInnTownId);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Time_gate_keeps_the_Shoujo_inn_and_relogin_uses_that_inn()
    {
        using var shop = new ShopFixture();
        var persistence = shop.Services.GetRequiredService<ISelectedSessionPositionPersistence>();
        var placement = shop.Services.GetRequiredService<IEntryPlacementRuleService>();
        Assert.True(await placement.CanRememberInnAsync(11, default));
        Assert.False(await placement.CanRememberInnAsync(12, default));
        await persistence.PersistAsync(shop.Session.AccountId, shop.Session.CharacterId, new(11, 0, 100, 200, 5), default);
        var timeGate = new CharacterPositionState(12, 0, 562, 183, 5);
        await persistence.PersistAsync(shop.Session.AccountId, shop.Session.CharacterId, timeGate, default);
        var loaded = await shop.Services.GetRequiredService<ICharacterDomainService>()
            .LoadByRosterPositionAsync(shop.Session.AccountId, 0, default);
        Assert.Equal(timeGate, loaded.Position);
        Assert.Equal(11, loaded.LastInnTownId);
        var general = await placement.ResolveAsync(new(10, 10010, ChannelEntryKind.General), loaded.LastInnTownId, default);
        Assert.Equal(11, general.Spawn.TownId);
        var restricted = await placement.ResolveAsync(new(100, 10100, ChannelEntryKind.SaintHorn), loaded.LastInnTownId, default);
        Assert.Equal(17, restricted.Spawn.TownId);
        Assert.False(restricted.PersistsPosition);
        Assert.Equal(11, (await shop.Services.GetRequiredService<ICharacterDomainService>()
            .LoadByRosterPositionAsync(shop.Session.AccountId, 0, default)).LastInnTownId);
    }
}
