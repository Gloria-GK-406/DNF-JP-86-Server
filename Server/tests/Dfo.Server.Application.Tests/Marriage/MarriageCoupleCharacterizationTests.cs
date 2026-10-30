using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>Characterization of B-14 (love points) and B-15 (guestbook).</summary>
public sealed class MarriageCoupleCharacterizationTests
{
    private const int Coupon = 10002901;
    private const int Other = 10002902;

    private sealed class LoveRules : IMarriageLovePointRuleService, IItemPlacementDefinitionSource
    {
        public ValueTask<MarriageLovePointItemRule?> RuleForAsync(int item, CancellationToken ct) => ValueTask.FromResult<MarriageLovePointItemRule?>(item == Coupon
            ? new(new(true, 0, 0, 1, ItemJobRestriction.Any, ItemPlaceRestriction.Anywhere, Action: "[love point coupon]"), 1, 999) : null);
        public ValueTask<ItemPlacement?> GetAsync(int item, CancellationToken ct) =>
            ValueTask.FromResult<ItemPlacement?>(new(2, 100, new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    [Fact]
    public async Task Love_point_refusals_answer_minus_one_two_or_three_by_cause()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var w = await ChurchTownAsync(configure: s =>
            {
                s.AddSingleton<IMarriageLovePointRuleService, LoveRules>();
                s.AddSingleton<IItemPlacementDefinitionSource, LoveRules>();
            });
            var f = w.F; var peer = w.Peer;
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            foreach (var actor in new[] { f.Session, peer })
                containers.Register(actor.CharacterId, new PersistentItemContainer(
                    ContainerId.ForCharacter(actor.CharacterId, InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
                    f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                    f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(f.Session.AccountId, f.Session.CharacterId,
                    [new(Coupon, 3), new(Other, 1)], "love-test", default, ItemDeliveryMode.InventoryOnly);
                await tx.CommitAsync(default);
            }
            var items = await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main).GetAllAsync(default);
            BusinessSlot SlotOf(int item) => A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, items.Single(x => x.ItemId == item).StorageSlotIndex));
            var coupon = SlotOf(Coupon); var other = SlotOf(Other);
            var couponStorage = items.Single(x => x.ItemId == Coupon).StorageSlotIndex;
            var empty = new BusinessSlot(BusinessInventorySpace.Consumable, 50);
            Task<MarriageLovePointResult> Use(SelectedSessionIdentity actor, BusinessSlot slot, params int[] accounts) =>
                InTx(f, () => TestEffectFootprints.Run(actor, cap => Couple(f).UseLovePointAsync(actor, slot, accounts.Select(x => new AccountId(x)).ToArray(), cap, default)));

            Assert.Equal(new[] { new AccountId(1) }, await Query(f).CoupleAccountsAsync(f.Session, default));
            Assert.Equal(-3, (await Use(f.Session, coupon, 1)).Status);
            var (ceremony, room) = await PreparedAsync(w);
            Assert.Equal(new[] { new AccountId(1), new AccountId(2) }, await Query(f).CoupleAccountsAsync(f.Session, default));
            Assert.Equal(-3, (await Use(f.Session, coupon, 1, 2)).Status);
            Arrive(f, f.Session, room); Arrive(f, peer, room);
            w.Clock.Now = room.ReadyUntil;
            await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default));
            Assert.Equal(-1, (await Use(f.Session, coupon, 1)).Status);
            Assert.Equal(-1, (await Use(f.Session with { ConnectionGeneration = f.Session.ConnectionGeneration + 1 }, coupon, 1, 2)).Status);
            Assert.Equal(-1, (await Use(f.Session, coupon with { Space = BusinessInventorySpace.Avatar }, 1, 2)).Status);
            Assert.Equal(-1, (await Use(f.Session, empty, 1, 2)).Status);
            Assert.Equal(-1, (await Use(f.Session, other, 1, 2)).Status);
            Assert.Equal(3, f.Scalar($"SELECT SUM(count) FROM item_slots WHERE owner_id=71 AND list_type=0 AND slot_index={couponStorage}"));
            var used = await Use(f.Session, coupon, 1, 2);
            Assert.Equal(0, used.Status); Assert.Equal(1, used.Points); Assert.Equal(peer.CharacterId, used.Partner);
            Assert.Equal(0, (await Query(f).SnapshotAsync(peer.AccountId, peer.CharacterId, default)).LovePoints);
            Assert.Equal(1, (await Query(f).SnapshotAsync(f.Session.AccountId, f.Session.CharacterId, default)).LovePoints);
            f.Sql("UPDATE marriage_personal_points SET points=998 WHERE character_id=71");
            Assert.Equal(999, (await Use(f.Session, coupon, 1, 2)).Points);
            Assert.Equal(-2, (await Use(f.Session, coupon, 1, 2)).Status);
            Assert.Equal(1, f.Scalar($"SELECT SUM(count) FROM item_slots WHERE owner_id=71 AND list_type=0 AND slot_index={couponStorage}"));
        });
    }

    [Fact]
    public async Task Guestbook_access_matrix_text_limits_and_capacity()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
            var (ceremony, room) = await PreparedAsync(w);
            var inside = AddCharacter(f, 3, 73, job: 0, gold: 100_000);
            var near = AddCharacter(f, 4, 74, job: 0, gold: 100_000);
            var stranger = AddCharacter(f, 5, 75, job: 0);
            var key = await InviteAsync(f, f.Session, inside);
            await PaidGuestAsync(f, inside, key, 10_000, room);
            await PaidGuestAsync(f, near, await InviteAsync(f, f.Session, near), 10_000);
            Task<MarriageGuestbookResult> Read(SelectedSessionIdentity actor, uint k) => InTx(f, () => Venue(f).ReadGuestbookAsync(actor, k, default));
            Task<int> Post(SelectedSessionIdentity actor, uint k, byte[] text) => InTx(f, () => Venue(f).PostGuestbookAsync(actor, k, text, default));
            Task<int> Delete(SelectedSessionIdentity actor, uint k, uint id) => InTx(f, () => Venue(f).DeleteGuestbookAsync(actor, k, id, default));

            Assert.Equal(0, (await Read(f.Session, 72)).Status);
            Assert.Equal(0, (await Read(peer, 71)).Status);
            Assert.Equal(-1, (await Read(f.Session, 71)).Status);
            Assert.Equal(-1, (await Read(f.Session, 0)).Status);
            Assert.Equal(-1, (await Read(f.Session, 0x8000_0000u)).Status);
            Assert.Equal(-1, await Post(f.Session, 72, "hi"u8.ToArray()));
            Assert.Equal(0, (await Read(inside, key)).Status);
            Assert.Equal(-1, (await Read(inside, 71)).Status);
            Assert.Equal(-1, await Post(inside, key, "hi"u8.ToArray()));
            Assert.Equal(0, (await Read(near, key)).Status);
            Arrive(f, near, room);
            Assert.Equal(-1, await Post(near, key, "hi"u8.ToArray()));
            Assert.Equal(-1, (await Read(stranger, key)).Status);
            Arrive(f, inside, room);
            var twenty = Enumerable.Repeat((byte)'a', 20).ToArray();
            Assert.Equal(0, await Post(inside, key, twenty));
            Assert.Equal(-1, await Post(inside, key, []));
            Assert.Equal(-1, await Post(inside, key, [(byte)'a', 0x1F]));
            Assert.Equal(-1, await Post(inside, key, [(byte)'a', 0x7F]));
            Assert.Equal(-1, await Post(inside, key, Enumerable.Repeat((byte)'a', 21).ToArray()));
            var written = Assert.Single((await Read(f.Session, 72)).Comments);
            Assert.Equal(new CharacterId(73), written.Author); Assert.Equal("char73"u8.ToArray(), written.AuthorName); Assert.Equal(twenty, written.Text);
            Assert.Equal(-1, await Delete(f.Session, 72, written.Id + 100));
            Assert.Equal(-1, await Delete(inside, key, written.Id));
            Assert.Equal(0, await Delete(peer, 71, written.Id));
            await using (var tx = f.Scope.Begin())
            {
                var store = f.Services.GetRequiredService<IMarriageGuestbookDomainStore>();
                for (var i = 0u; i < 100; i++) await store.AddAsync(ceremony.RelationId, new(73), "char73"u8.ToArray(), "x"u8.ToArray(), i, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(-1, await Post(inside, key, "hi"u8.ToArray()));
            Assert.Equal(100, (await Read(f.Session, 72)).Comments.Count);
            w.Clock.Now = room.ReadyUntil;
            Assert.Equal(MarriageWeddingPhase.Cancelled, (await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default)))!.Wedding.Phase);
            Assert.Equal(-1, (await Read(inside, key)).Status);
            Assert.Equal(0, (await Read(f.Session, 72)).Status);
        });
    }
}
