using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

/// <summary>
/// An effect request through the real dispatcher settles on the executor, and so does the replay of
/// its lost acknowledgement (spec appendix A row 2, appendix H row 2).
/// </summary>
public sealed class EffectDispatchOnExecutorTests
{
    [Fact]
    public async Task An_effect_request_and_its_replay_both_run_as_the_inbound_job()
    {
        using var f = new ShopFixture(services => services.AddSingleton<IItemUseRuleDefinitionSource>(
            new FakeItemUseRules((1000, ItemUseRules.Potion(0)))));
        await f.Seed(69, 1000, 3);
        var seen = new List<(string Hook, bool OnExecutor, string? CommandType)>();
        f.Sender.BeforeExplicit = () => seen.Add(("explicit", GameplayJob.IsOnExecutor, GameplayJob.Current?.CommandType));
        f.Sender.BeforeSend = () => seen.Add(("send", GameplayJob.IsOnExecutor, GameplayJob.Current?.CommandType));
        f.Sender.FailExplicit = true;
        var counter = new GameInboundFrameCounter();
        UseStackableRequest Request()
        {
            var (ordinal, fresh) = counter.Observe(1);
            var body = new byte[11];
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), 69);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), 1000);
            return new((new GameMessageMetadata(1, 0x002C, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(body),
                new(InventoryContainerType.Main, 69), 0, 1000);
        }
        var dispatcher = f.Services.GetRequiredService<INetworkMessageDispatcher>();

        await Assert.ThrowsAsync<IOException>(async () => await dispatcher.DispatchAsync(new(f.Connection), Request(), default));
        Assert.Equal(2, f.Count(1000));
        await dispatcher.DispatchAsync(new(f.Connection), Request(), default);

        Assert.Equal(2, f.Count(1000));
        Assert.Single(f.Sender.Batches);
        Assert.Contains(seen, x => x.Hook == "explicit");
        Assert.Contains(seen, x => x.Hook == "send");
        Assert.All(seen, x =>
        {
            Assert.True(x.OnExecutor);
            Assert.Equal($"inbound:{nameof(ClientMessageType.GameUseStackable)}", x.CommandType);
        });
    }
}
