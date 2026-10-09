using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Teleport;
using Dfo.Server.Abstractions.Networking;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class TeleportConsumableTests
{
    private static readonly CharacterPositionState Start = new(0, 0, 12, 34, 5);
    private static readonly CharacterPositionState Destination = new(11, 3, 474, 234, 0);
    private static ShopFixture Fixture()
    {
        var f = new ShopFixture(s =>
        {
            s.AddSingleton<ITeleportConsumableDefinitionSource>(new Definitions());
            s.AddSingleton<IItemUseRuleDefinitionSource>(new UseRules());
            s.AddSingleton<ITownDefinitionSource>(new Towns());
            s.AddSingleton<ISelectedCharacterCurrentViewDomainService>(new CurrentView());
        });
        f.SetLevel(2);
        f.Services.GetRequiredService<ICharacterPositionDomainService>().Place(f.Session, Start);
        return f;
    }
    private static ValueTask Send(ShopFixture f, ushort sequence = 7, short slot = 65, int item = 2000, byte town = 11,
        byte reserved = 0, short x = short.MaxValue) => EffectRequestTransport.Send(f,
            new TeleportRequest(new(1, 0x00ED, 0, sequence), slot, item, reserved, town, 0, x, short.MaxValue));

    [Theory]
    [InlineData(65)]
    [InlineData(3)]
    public async Task Potion_consumption_position_and_cooldown_commit_then_only_current_state_replays(short slot)
    {
        using var f = Fixture();
        await f.Seed(slot, 2000, 2);
        f.Sender.BeforeSend = () =>
        {
            Assert.Equal(1, f.Count(2000));
            Assert.Equal(11, f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
            Assert.Equal(Destination, f.Services.GetRequiredService<ICharacterPositionDomainService>().Find(f.Session.CharacterId));
        };
        await Send(f, slot: slot);
        Assert.Equal(Destination, f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Position);
        Assert.False(f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.SharesTownPresence);
        Assert.Single(f.Sender.Messages.OfType<TeleportCooldownNotification>());
        await Send(f, slot: slot);
        Assert.Equal(1, f.Count(2000));
        Assert.Single(f.Sender.Messages.OfType<TeleportCooldownNotification>());
        await Send(f, sequence: 8, slot: slot);
        Assert.Equal(1, f.Count(2000));
        Assert.Single(f.Sender.Messages.OfType<TeleportResponse>());
        f.Sender.BeforeSend = null;
        f.Clock.Advance(TimeSpan.FromSeconds(11));
        await Send(f, sequence: 9, slot: slot);
        Assert.Equal(0, f.Count(2000));
    }

    [Fact]
    public async Task Position_write_failure_rolls_back_consumption_memory_and_cooldown_and_same_request_can_retry()
    {
        using var f = Fixture(); await f.Seed(65, 2000, 2);
        f.Sql("CREATE TRIGGER fail_teleport BEFORE UPDATE OF town_id ON characters BEGIN SELECT RAISE(ABORT,'teleport failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Send(f).AsTask());
        Assert.Equal(2, f.Count(2000));
        Assert.Equal(0, f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
        Assert.Equal(Start, f.Services.GetRequiredService<ICharacterPositionDomainService>().Find(f.Session.CharacterId));
        Assert.False(f.Services.GetRequiredService<ICharacterItemCooldownDomainService>().IsCoolingDown(f.Session.CharacterId, ItemCooldownKey.ForItem(2000)));
        Assert.Empty(f.Sender.Messages);
        f.Sql("DROP TRIGGER fail_teleport"); await Send(f);
        Assert.Equal(1, f.Count(2000)); Assert.Equal(Destination, f.Services.GetRequiredService<ICharacterPositionDomainService>().Find(f.Session.CharacterId));
    }

    [Fact]
    public async Task Old_teleport_replay_after_another_move_projects_only_the_current_location()
    {
        using var f = Fixture(); await f.Seed(65, 2000, 2);
        await Send(f);
        var current = new CharacterPositionState(2, 3, 100, 200, 1);
        var areas = f.Services.GetRequiredService<ITownAreaDomainService>();
        areas.TransferExact(f.Session.CharacterId, f.Session.TownAreaLease(), new(1, 2, 3), current, false);
        f.SetLevel(2);
        f.Services.GetRequiredService<ICharacterPositionDomainService>().Place(f.Session, current);
        f.Sender.Messages.Clear();
        await Send(f);
        Assert.Equal(1, f.Count(2000));
        Assert.Equal(current, areas.Find(f.Session.CharacterId)!.Position);
        Assert.Equal(current, f.Services.GetRequiredService<ICharacterPositionDomainService>().Find(f.Session.CharacterId));
        var projected = Assert.Single(f.Sender.Messages, x => x.MessageType == ServerMessageType.GameUserArea);
        var registration = f.Services.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == projected.GetType());
        var frame = Assert.Single(((IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType)).Encode(projected)).ToArray();
        Assert.Equal(new byte[] { current.TownId, current.AreaId, 100, 0, 200, 0 }, frame[17..23]);
        Assert.Empty(f.Sender.Messages.OfType<TeleportCooldownNotification>());
    }

    [Fact]
    public async Task Lost_notification_replay_repairs_town_membership_without_consuming_again()
    {
        using var f = Fixture(); await f.Seed(65, 2000, 2);
        f.Sender.FailBatch = true;
        await Assert.ThrowsAsync<IOException>(() => Send(f).AsTask());
        Assert.Equal(1, f.Count(2000)); Assert.Equal(Destination, f.Services.GetRequiredService<ICharacterPositionDomainService>().Find(f.Session.CharacterId));
        Assert.Equal(Destination, f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Position);
        f.Sender.FailBatch = false; await Send(f);
        Assert.Equal(1, f.Count(2000));
        Assert.Equal(Destination, f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Position);
    }

    [Theory]
    [InlineData(2001, 11, 0, 32767)]
    [InlineData(2000, 1, 0, 32767)]
    [InlineData(2000, 11, 1, 32767)]
    [InlineData(2000, 11, 0, 100)]
    public async Task Invalid_item_destination_or_wire_variant_preserves_assets(int item, byte town, byte reserved, short x)
    {
        using var f = Fixture(); await f.Seed(65, 2000, 2);
        await Send(f, item: item, town: town, reserved: reserved, x: x);
        Assert.Equal(2, f.Count(2000)); Assert.Equal(Start, f.Services.GetRequiredService<ICharacterPositionDomainService>().Find(f.Session.CharacterId));
    }

    [Fact]
    public async Task Destination_level_requirement_and_dungeon_state_refuse_without_consuming()
    {
        using var f = Fixture(); await f.Seed(65, 2000, 2);
        f.Sql($"UPDATE characters SET level=1,exp={f.ExperienceForLevel(1)} WHERE character_id=1"); await Send(f);
        Assert.Equal(2, f.Count(2000));
        f.Sql($"UPDATE characters SET level=2,exp={f.ExperienceForLevel(2)} WHERE character_id=1");
        var cell = new MazeCell(0, 0);
        f.Services.GetRequiredService<IDungeonRunDomainService>().PreparePartyEntry(
            [new(f.Session.AccountId, f.Session.CharacterId, 0, true)],
            new(144, 0, 1, 1, cell, cell, 55611, 0, new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(55611)}), 0);
        await Send(f, sequence: 8); Assert.Equal(2, f.Count(2000));
        Assert.Equal(Start, f.Services.GetRequiredService<ICharacterPositionDomainService>().Find(f.Session.CharacterId));
    }

    private sealed class Definitions : ITeleportConsumableDefinitionSource
    {
        public ValueTask<TeleportConsumableDefinition?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<TeleportConsumableDefinition?>(itemId == 2000 ? new([11]) : null);
    }
    private sealed class UseRules : IItemUseRuleDefinitionSource
    {
        public ValueTask<ItemUseRule?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<ItemUseRule?>(new(true, 10000, 0, 1, ItemJobRestriction.Any, ItemPlaceRestriction.Anywhere, false));
    }
    private sealed class Towns : ITownDefinitionSource
    {
        public ValueTask<TownDefinition?> GetAsync(int town, CancellationToken ct) =>
            ValueTask.FromResult<TownDefinition?>(town == 11 ? new(11, "素喃", [new TownAreaDefinition(3, "gate.map", TownAreaKind.Gate, 474, 234) { MinimumLevel = 2 }]) : null);
        public ValueTask<IReadOnlyList<TownDefinition>> GetAllAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<MapGeometry?> GetMapGeometryAsync(string map, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class CurrentView : ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken ct) =>
            ValueTask.FromResult<SelectCharacterResult?>(new(null!, null!, null!, null!, null!,
                new(DateTimeOffset.UnixEpoch, null!, new(1, new(0, 0, 12, 34, 5, 3, false), true, false)), null!));
    }
}
