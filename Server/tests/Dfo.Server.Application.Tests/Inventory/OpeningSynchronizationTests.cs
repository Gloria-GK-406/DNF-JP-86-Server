using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CompoundItem;
using Dfo.Server.Protocol.Game.MagicBoxBatchOpening;
using Dfo.Server.Protocol.Game.PackageOpening;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class OpeningSynchronizationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Package_delta_converges_when_reward_reuses_exhausted_source_or_an_empty_slot(int count)
    {
        using var opening = new OpeningFixture();
        await opening.Host.Seed(3, 1001, count);
        var result = await opening.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new PackageOpeningCommand(opening.Host.OnlineUser, opening.Host.Session, new(3, [])));
        var messages = await Compose(opening.Host, result.Receipt!, new PackageOpeningResponse(3, result));
        Assert.IsType<PackageOpeningResponse>(messages[0]);
        var client = new MainInventoryReplica((3, 1001, count));
        client.Receive(opening.Host, messages);
        Assert.Equal(count - 1, client.Count(1001));
        Assert.Equal(opening.Host.Count(3), client.Count(3));
        Assert.Equal(2, client.Count(3));
    }

    [Fact]
    public async Task Existing_reward_pile_is_updated_and_exhausted_source_is_removed()
    {
        using var opening = new OpeningFixture();
        await opening.Host.Seed(3, 1001, 1);
        await opening.Host.Seed(4, 3, 5);
        var result = await opening.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new PackageOpeningCommand(opening.Host.OnlineUser, opening.Host.Session, new(3, [])));
        var messages = await Compose(opening.Host, result.Receipt!, new PackageOpeningResponse(3, result));
        var client = new MainInventoryReplica((3, 1001, 1), (4, 3, 5));
        client.Receive(opening.Host, messages);
        Assert.Equal(0, client.Count(1001));
        Assert.Equal(7, client.Count(3));
    }

    [Fact]
    public async Task Magic_box_can_replace_both_consumed_piles_even_after_native_ack_removes_them()
    {
        using var opening = new OpeningFixture();
        await opening.Host.Seed(3, 1000, 1);
        await opening.Host.Seed(4, 2000, 1);
        var input = new MagicBoxOpeningInput(0, 3, 1000, 4, 2000, 1);
        var result = await opening.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new MagicBoxBatchOpeningCommand(opening.Host.OnlineUser, opening.Host.Session, input));
        var messages = await Compose(opening.Host, result.Receipt!, new MagicBoxBatchOpeningResponse(input, result));
        Assert.IsType<MagicBoxBatchOpeningResponse>(messages[0]);
        // Native magic-box ACK removes these piles before the absolute deltas arrive.
        var client = new MainInventoryReplica();
        client.Receive(opening.Host, messages);
        Assert.Equal(opening.Host.Count(3), client.Count(3));
        Assert.Equal(opening.Host.Count(6000), client.Count(6000));
        Assert.Equal(2, client.Count(3));
        Assert.Equal(1, client.Count(6000));
    }

    [Fact]
    public async Task Recipe_and_material_slots_reused_by_multiple_outputs_converge()
    {
        var content = new OpeningContent();
        using var host = new ShopFixture(services =>
        {
            services.AddSingleton<IRecipeContentSource>(new Recipe());
            services.AddSingleton<IPackageOpeningDefinitionSource>(content);
            services.AddSingleton<IItemPlacementDefinitionSource>(content);
            services.AddSingleton<IItemUseRuleDefinitionSource>(content);
        });
        await host.Seed(3, 1001, 1);
        await host.Seed(4, 2000, 1);
        var result = await host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new CompoundItemCommand(host.OnlineUser, host.Session, new(BusinessInventorySpace.QuickSlot, 0), 1));
        var messages = await Compose(host, result.Inventory, new CompoundItemResponse(result));
        var client = new MainInventoryReplica((3, 1001, 1), (4, 2000, 1));
        client.Receive(host, messages);
        Assert.Equal(0, client.Count(1001));
        Assert.Equal(0, client.Count(2000));
        Assert.Equal(2, client.Count(3));
        Assert.Equal(1, client.Count(6000));
    }

    private sealed class Recipe : IRecipeContentSource
    {
        public ValueTask<RecipePlan?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<RecipePlan?>(id == 1001 ? new([new(2000, 1)], [new(3, 2), new(6000, 1)]) : null);
    }

    private static async Task<IReadOnlyList<IServerNetworkMessage>> Compose(ShopFixture host,
        OpeningInventoryReceipt receipt, IGameCmdMessage response) =>
        await TestGameplay.RunAsync(host.Services, async () => await new OpeningCompletedHandler(host.Services.GetRequiredService<ISelectedSessionDomainService>())
            .ComposeAsync(new(receipt.Session.CharacterId, receipt.Session.ConnectionId), new(receipt, [response]), default));

    // Bounded model of the A21 class-1 stackable consumer, not a native-client replay.
    // 0117E91E clears ID=-1; 0117E957 checks template; 0117EAF4 ignores differing IDs.
    private sealed class MainInventoryReplica(params (int Slot, int Id, int Count)[] initial)
    {
        private readonly Dictionary<int, (int Id, int Count)> slots = initial.ToDictionary(x => x.Slot, x => (x.Id, x.Count));
        public int Count(int id) => slots.Values.Where(x => x.Id == id).Sum(x => x.Count);
        public void Receive(ShopFixture host, IEnumerable<IServerNetworkMessage> messages)
        {
            foreach (var message in messages)
            {
                var registration = host.Services.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == message.GetType());
                var encoder = (IServerMessageEncoder)host.Services.GetRequiredService(registration.EncoderType);
                foreach (var frame in encoder.Encode(message))
                {
                    if (frame[0] != 0 || BitConverter.ToUInt16(frame, 1) != 0x000E || frame[15] != 0) continue;
                    var count = BitConverter.ToUInt16(frame, 16);
                    Assert.Equal(18 + count * 101, frame.Length);
                    for (var i = 0; i < count; i++)
                    {
                        var start = 18 + i * 101;
                        var slot = BitConverter.ToInt16(frame, start);
                        var id = BitConverter.ToInt32(frame, start + 2);
                        var quantity = BitConverter.ToInt32(frame, start + 6);
                        if (id == -1) slots.Remove(slot);
                        else if (!slots.TryGetValue(slot, out var old) || old.Id == id) slots[slot] = (id, quantity);
                    }
                }
            }
        }
    }
}
