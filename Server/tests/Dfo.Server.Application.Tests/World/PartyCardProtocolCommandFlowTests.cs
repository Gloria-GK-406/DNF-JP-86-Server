using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Quests;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Tests.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class CardSettlementLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Party_clear_reveals_each_members_own_free_draw_and_pays_it_once(bool peerAutoFlips)
    {
        var calculation = new CommonExperienceEffectTests.Calculation();
        using var f = new ShopFixture(services =>
        {
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>();
            services.AddSingleton<IExperienceRuleService>(new OrdinaryQuestEffectTests.FixedProgression());
            services.AddSingleton<IDungeonDifficultyRuleService>(new Support.TestDungeonDifficultyRules());
            services.AddSingleton<IClearRewardRuleService, PartyCardRules>();
            services.AddSingleton<IEquipmentDefinitionSource, TowerRewardTestContent>();
            services.AddSingleton<IDungeonCardPresentationDelay>(new Delay());
            services.AddSingleton<IExperienceCalculationDomainService>(p =>
            { calculation.Transactions = p.GetRequiredService<IDomainTransactionAccessor>(); return calculation; });
        });
        var peer = await TestGameplay.RunAsync(f.Services, () => TowerSourceMembers.AddAsync(f, 3, 2, 1, 4000));
        var peerConnection = TestGameplay.Run(f.Services, () =>
        { f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId, out var user); return user!.Connection; });
        f.Sql("UPDATE character_stats SET carried_gold=100 WHERE character_id IN (1,3)");
        Assert.True(f.Services.GetRequiredService<IGoldCarryLimitSource>().BaseLimitFor(1) >= 111);
        var runId = CharacterDeathProtocolLifecycleTests.Enter(f, peer);
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.DungeonRuns();
            Assert.NotNull(runs.TryOpenClear(f.Session.CharacterId, runId, runs.Find(f.Session.CharacterId)!.RoomVersion));
        });
        await WireOn(f, f.Connection, 0x2E, new byte[53]);
        await WireOn(f, peerConnection, 0x2E, new byte[53]);
        Assert.Equal(30, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(30, f.Scalar("SELECT exp FROM characters WHERE character_id=3"));
        Assert.Equal(2, calculation.Calls.Count); Assert.All(calculation.Calls, call => Assert.True(call.InTransaction));
        await WireOn(f, f.Connection, 0x2E, new byte[53]);
        Assert.Equal(2, calculation.Calls.Count);
        foreach (var connection in new[] { f.Connection, peerConnection })
        {
            var frame = Assert.Single(f.Sender.Outbound.Entries
                .Where(entry => entry.Connection.ConnectionId == connection.ConnectionId)
                .SelectMany(f.Sender.Outbound.FramesOf),
                bytes => bytes[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1)) == 0x23);
            var row = CardPanelFrameReader.ReadClearDungeonReward(frame.AsSpan(GameServerFrameEncoder.HeaderLength)).FreeRow;
            Assert.Equal(7, row.Columns[0].Gold);
            Assert.Equal(new CardEntry(2000, 1), row.Columns[0].Piece);
            Assert.Equal(11, row.Columns[1].Gold);
            Assert.Equal(new CardEntry(3000, 1), row.Columns[1].Piece);
        }

        await WireOn(f, f.Connection, 0x46, []);
        await WireOn(f, peerConnection, 0x46, []);
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.DungeonRuns();
            foreach (var actor in new[] { f.Session, peer })
            {
                var current = runs.Find(actor.CharacterId)!;
                Assert.True(current.CardLayoutOpen, $"Card layout not opened for {actor.CharacterId}, settled={current.IsSettled}");
                Assert.Equal(actor == f.Session ? 7 : 11, current.ClearRewardCards!.Free.Gold);
                Assert.NotNull(current.ClearRewardCards.BlackDiamond);
                Assert.Contains(actor, Dfo.Server.Protocol.Game.Dungeons.DungeonPartyRecipients.FindCurrent(
                    f.Services.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>(), f.Services.GetRequiredService<ISelectedSessionDomainService>(),
                    runs, f.Services.GetRequiredService<IDungeonRunProjectionDomainService>(), actor, runId));
            }
        });
        f.Sender.Outbound.Clear();
        await WireOn(f, f.Connection, 0x47, [0, 0]);
        Assert.Equal(107, f.Gold);
        Assert.Equal(1, f.Count(2000));
        Assert.Equal(0, f.Count(3000));
        Assert.Equal(1, f.Count(4000));
        Assert.Empty(f.Sender.Outbound.MessagesFor(peer.ConnectionId));
        f.Sender.Outbound.Clear();
        if (peerAutoFlips)
        {
            var bus = f.Services.GetRequiredService<ICommandBus>();
            await bus.SendInternalAsync(new DungeonCardAutoFlipCommand(peer, runId, default),
                new InternalCommandOrigin(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
                    "timer:DungeonCardPresentationCoordinator/auto-flip", InternalCommandCancellation.BodyOnly), default);
        }
        else
        {
            await WireOn(f, peerConnection, 0x47, [0, 0]);
        }

        Assert.Equal(111, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        Assert.Equal(0, f.Scalar("SELECT COALESCE(sum(s.count),0) FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE s.owner_id=3 AND s.owner_scope='character' AND substr(hex(i.item_core),3,8)='D0070000'"));
        Assert.Equal(1, f.Scalar("SELECT COALESCE(sum(s.count),0) FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE s.owner_id=3 AND s.owner_scope='character' AND substr(hex(i.item_core),3,8)='B80B0000'"));
        Assert.Equal(4, f.Scalar("SELECT COALESCE(sum(s.count),0) FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE s.owner_id=3 AND s.owner_scope='character' AND substr(hex(i.item_core),3,8)='A00F0000'"));
        var shared = f.Sender.Outbound.Entries.Where(e => e.Messages.OfType<SelectCardResponse>().Any()).ToArray();
        Assert.Equal(new[] { peer.ConnectionId, f.Session.ConnectionId }, shared.Select(e => e.Connection.ConnectionId));
        Assert.All(shared, entry => Assert.Equal(2, Assert.Single(entry.Messages.OfType<SelectCardResponse>()).Selections.Count));
        Assert.Equal(new ushort[] { 0x47, 0x0E, 0x0E, 0x0E, 0x036F }, CharacterDeathOracle.Types(shared[0].Frames.ToArray()));
        Assert.Equal(new ushort[] { 0x47 }, CharacterDeathOracle.Types(shared[1].Frames.ToArray()));
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.DungeonRuns();
            Assert.True(runs.Find(f.Session.CharacterId)!.ClearRewardCards!.IsClaimed(ClearRewardCardKind.Free, 0));
            Assert.True(runs.Find(peer.CharacterId)!.ClearRewardCards!.IsClaimed(ClearRewardCardKind.Free, 0));
        });
        var count = f.Sender.Outbound.Entries.Count;
        await WireOn(f, peerConnection, 0x47, [0, 0]);
        Assert.Equal(111, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        Assert.Equal(count, f.Sender.Outbound.Entries.Count); Assert.Empty(f.Sender.Outbound.Closes);
    }
    private sealed class PartyCardRules : IClearRewardRuleService
    {
        private readonly Dictionary<ulong, int> freeDraws = [];

        public ClearRewardChance ChanceOf(ClearRewardOccasion occasion) => new(1, 1);

        public ClearRewardCardFace FaceFor(ClearRewardOccasion occasion, ulong seed)
        {
            if (occasion.Kind == ClearRewardCardKind.Free)
            {
                // Give each seed a repeatable, distinguishable reward in this controlled pool.
                if (!this.freeDraws.TryGetValue(seed, out var draw))
                {
                    draw = this.freeDraws.Count;
                    this.freeDraws.Add(seed, draw);
                }

                return new(occasion.Kind, [new(0, new(2000 + 1000 * draw, 1, DropRarity.Common, 0))], 7 + 4 * draw);
            }

            return new(occasion.Kind,
                [new(0, new(occasion.Kind == ClearRewardCardKind.BlackDiamond ? 4000 : 2000, 1, DropRarity.Common, 0))],
                occasion.Kind == ClearRewardCardKind.Paid ? 11 : 0);
        }

        public int? PaidFlipCost(int dungeonBasisLevel) => 13;
        public ValueTask<bool> OffersPaidFlipAsync(int dungeonId, CancellationToken ct) => ValueTask.FromResult(true);
    }
}
