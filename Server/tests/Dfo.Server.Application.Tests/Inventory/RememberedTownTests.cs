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
    [Theory]
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
        var gate = (await shop.Services.GetRequiredService<ITownDefinitionSource>().GetAsync(town, default))!.Gate!;
        var placement = await shop.Services.GetRequiredService<IEntryPlacementRuleService>()
            .ResolveAsync(new(10, 10011, ChannelEntryKind.General), loaded.Position.TownId, default);
        Assert.Equal(town, placement.Spawn.TownId);
        Assert.Equal(gate.AreaId, placement.Spawn.AreaId);
        Assert.Equal(gate.Operand1, placement.Spawn.X);
        Assert.Equal(gate.Operand2, placement.Spawn.Y);
        Assert.False(placement.Spawn.SharesTownPresence);
        var other = await shop.Services.GetRequiredService<ICharacterDomainService>()
            .LoadByRosterPositionAsync(shop.Session.AccountId, 1, default);
        Assert.Equal(0, other.Position.TownId);
    }
}
