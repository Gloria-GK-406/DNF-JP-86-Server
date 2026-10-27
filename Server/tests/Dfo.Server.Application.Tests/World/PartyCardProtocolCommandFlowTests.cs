using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Quests;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class CardSettlementLifecycleTests
{
    [Fact]
    public async Task Real_party_clear_and_flip_pay_each_member_once_and_publish_the_last_shared_board_in_XO2_order()
    {
        var calculation = new CommonExperienceEffectTests.Calculation();
        using var f = new ShopFixture(services =>
        {
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, EntryFailureProbe.CompleteView>();
            services.AddSingleton<IExperienceRuleService>(new OrdinaryQuestEffectTests.FixedProgression());
            services.AddSingleton<IDungeonDifficultyRuleService>(new Support.TestDungeonDifficultyRules());
            services.AddSingleton<IClearRewardRuleService, PartyCardRules>();
            services.AddSingleton<IDungeonCardPresentationDelay>(new Delay());
            services.AddSingleton<IExperienceCalculationDomainService>(p =>
            { calculation.Transactions = p.GetRequiredService<IDomainTransactionAccessor>(); return calculation; });
        });
        var peer = await TestGameplay.RunAsync(f.Services, () => TowerSourceMembers.AddAsync(f, 3, 2, 1, 4000));
        var peerConnection = TestGameplay.Run(f.Services, () =>
        { f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId, out var user); return user!.Connection; });
        f.Sql("UPDATE character_stats SET carried_gold=100 WHERE character_id IN (1,3)");
        Assert.True(f.Services.GetRequiredService<IGoldCarryLimitSource>().BaseLimitFor(1) >= 107);
        var runId = CharacterDeathProtocolLifecycleTests.Enter(f, peer);
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            Assert.NotNull(runs.TryOpenClear(f.Session.CharacterId, runId, runs.Find(f.Session.CharacterId)!.RoomVersion));
        });
        await WireOn(f, f.Connection, 0x2E, new byte[53]);
        await WireOn(f, peerConnection, 0x2E, new byte[53]);
        Assert.Equal(30, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(30, f.Scalar("SELECT exp FROM characters WHERE character_id=3"));
        Assert.Equal(2, calculation.Calls.Count); Assert.All(calculation.Calls, call => Assert.True(call.InTransaction));
        await WireOn(f, f.Connection, 0x2E, new byte[53]);
        Assert.Equal(2, calculation.Calls.Count);
        await WireOn(f, f.Connection, 0x46, []);
        await WireOn(f, peerConnection, 0x46, []);
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            foreach (var actor in new[] { f.Session, peer })
            {
                var current = runs.Find(actor.CharacterId)!;
                Assert.True(current.CardLayoutOpen, $"Card layout not opened for {actor.CharacterId}, settled={current.IsSettled}");
                Assert.Equal(7, current.ClearRewardCards!.Free.Gold);
                Assert.NotNull(current.ClearRewardCards.BlackDiamond);
                Assert.Contains(actor, Dfo.Server.Protocol.Game.Dungeons.DungeonPartyRecipients.FindCurrent(
                    f.Services.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>(), f.Services.GetRequiredService<ISelectedSessionDomainService>(),
                    runs, f.Services.GetRequiredService<IDungeonRunProjectionDomainService>(), actor, runId));
            }
        });
        f.Sender.Outbound.Clear();
        await WireOn(f, f.Connection, 0x47, [0, 0]);
        Assert.Equal(107, f.Gold); Assert.Equal(1, f.Count(2000)); Assert.Equal(1, f.Count(4000));
        Assert.Empty(f.Sender.Outbound.MessagesFor(peer.ConnectionId));
        f.Sender.Outbound.Clear();
        await WireOn(f, peerConnection, 0x47, [0, 0]);
        Assert.Equal(107, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        Assert.Equal(1, f.Scalar("SELECT COALESCE(sum(s.count),0) FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE s.owner_id=3 AND s.owner_scope='character' AND substr(hex(i.item_core),3,8)='D0070000'"));
        Assert.Equal(4, f.Scalar("SELECT COALESCE(sum(s.count),0) FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE s.owner_id=3 AND s.owner_scope='character' AND substr(hex(i.item_core),3,8)='A00F0000'"));
        var shared = f.Sender.Outbound.Entries.Where(e => e.Messages.OfType<SelectCardResponse>().Any()).ToArray();
        Assert.Equal(new[] { peer.ConnectionId, f.Session.ConnectionId }, shared.Select(e => e.Connection.ConnectionId));
        Assert.All(shared, entry => Assert.Equal(2, Assert.Single(entry.Messages.OfType<SelectCardResponse>()).Selections.Count));
        Assert.Equal(new ushort[] { 0x47, 0x0E, 0x0E, 0x0E, 0x036F }, CharacterDeathOracle.Types(shared[0].Frames.ToArray()));
        Assert.Equal(new ushort[] { 0x47 }, CharacterDeathOracle.Types(shared[1].Frames.ToArray()));
        TestGameplay.Run(f.Services, () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            Assert.True(runs.Find(f.Session.CharacterId)!.ClearRewardCards!.IsClaimed(ClearRewardCardKind.Free, 0));
            Assert.True(runs.Find(peer.CharacterId)!.ClearRewardCards!.IsClaimed(ClearRewardCardKind.Free, 0));
        });
        var count = f.Sender.Outbound.Entries.Count;
        await WireOn(f, peerConnection, 0x47, [0, 0]);
        Assert.Equal(107, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        Assert.Equal(count, f.Sender.Outbound.Entries.Count); Assert.Empty(f.Sender.Outbound.Closes);
    }
    private sealed class PartyCardRules : IClearRewardRuleService
    {
        public ClearRewardChance ChanceOf(ClearRewardOccasion occasion) => new(1, 1);
        public ClearRewardCardFace FaceFor(ClearRewardOccasion occasion, ulong seed) => new(occasion.Kind, [new(0, new(occasion.Kind == ClearRewardCardKind.BlackDiamond ? 4000 : 2000, 1, DropRarity.Common, 0))],
            occasion.Kind == ClearRewardCardKind.Free ? 7 : occasion.Kind == ClearRewardCardKind.Paid ? 11 : 0);
        public int? PaidFlipCost(int dungeonBasisLevel) => 13;
        public ValueTask<bool> OffersPaidFlipAsync(int dungeonId, CancellationToken ct) => ValueTask.FromResult(true);
    }
}
