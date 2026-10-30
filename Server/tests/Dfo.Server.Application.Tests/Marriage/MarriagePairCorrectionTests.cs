using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

/// <summary>
/// Before/after cases for the unified pair check (corrections B1-1, B1-2, B1-4, B1-5 in the marriage aggregate design).
/// Assertions state the corrected behaviour; each case name says what the uncorrected code did instead.
/// </summary>
public sealed class MarriagePairCorrectionTests
{
    private const int Coupon = 10002901;

    private sealed class LoveRules : IMarriageLovePointRuleService, IItemPlacementDefinitionSource
    {
        public ValueTask<MarriageLovePointItemRule?> RuleForAsync(int item, CancellationToken ct) => ValueTask.FromResult<MarriageLovePointItemRule?>(item == Coupon
            ? new(new(true, 0, 0, 1, ItemJobRestriction.Any, ItemPlaceRestriction.Anywhere, Action: "[love point coupon]"), 1, 999) : null);
        public ValueTask<ItemPlacement?> GetAsync(int item, CancellationToken ct) =>
            ValueTask.FromResult<ItemPlacement?>(new(2, 100, new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    private sealed class SwitchableContent : IMarriageProposalContentSource
    {
        public bool Missing { get; set; }
        public MarriageProposalContent Load() => Missing ? throw new FileNotFoundException("Script.pvf was not found.") : new(30);
    }

    /// <summary>Takes <paramref name="owner"/> offline and selects its character under another account.</summary>
    private static SelectedSessionIdentity Impostor(LearningFixture f, SelectedSessionIdentity owner, int account)
    {
        GoOffline(f, owner);
        f.Sql($"INSERT INTO accounts(account_id,m_id) VALUES({account},'acct{account}');");
        var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
        var connection = new NetworkConnectionContext(new($"marriage-impostor-{account}"), NetworkEndpointKind.Game, 10011);
        var user = registry.RegisterConnection(connection).OnlineUserId;
        registry.Authenticate(user, new(account));
        var session = SelectedSessionIdentity.Create(new(account), owner.CharacterId, connection.ConnectionId,
            registry.GetRequiredUser(user).Generation, 10011);
        registry.Select(session);
        registry.EnterWorld(session);
        f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(session);
        var position = new CharacterPositionState(2, 1, 100, 200, 0);
        f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(session, position, 100);
        f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new(session, new(1, 2, 1), position, true));
        return session;
    }

    [Fact]
    public async Task B1_1_love_points_refuse_an_account_that_does_not_own_the_spouse_where_the_character_alone_was_enough()
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
            containers.Register(f.Session.CharacterId, new PersistentItemContainer(
                ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
                f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(f.Session.AccountId, f.Session.CharacterId,
                    [new(Coupon, 3)], "love-test", default, ItemDeliveryMode.InventoryOnly);
                await tx.CommitAsync(default);
            }
            var stored = (await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main).GetAllAsync(default)).Single();
            var coupon = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, stored.StorageSlotIndex));
            var (ceremony, room) = await PreparedAsync(w);
            Arrive(f, f.Session, room); Arrive(f, peer, room);
            w.Clock.Now = room.ReadyUntil;
            await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default));
            Assert.Equal(new[] { new AccountId(1), new AccountId(2) }, await Query(f).CoupleAccountsAsync(f.Session, default));

            var impostor = Impostor(f, f.Session, 9);
            Task<MarriageLovePointResult> Use(params int[] accounts) =>
                InTx(f, () => TestEffectFootprints.Run(impostor, cap => Couple(f).UseLovePointAsync(impostor, coupon, accounts.Select(x => new AccountId(x)).ToArray(), cap, default)));

            Assert.Equal(new[] { new AccountId(9) }, await Query(f).CoupleAccountsAsync(impostor, default));
            Assert.Equal(-1, (await Use(1, 2)).Status);
            Assert.Equal(-3, (await Use(9)).Status);
            Assert.Equal(3, f.Scalar($"SELECT SUM(count) FROM item_slots WHERE owner_id=71 AND list_type=0 AND slot_index={stored.StorageSlotIndex}"));
            Assert.Equal(new[] { new AccountId(1), new AccountId(2) }, await Query(f).CoupleAccountsAsync(peer, default));
        });
    }

    [Fact]
    public async Task B1_2_guestbook_owner_path_refuses_an_account_that_does_not_own_the_newlywed_where_the_character_alone_was_enough()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var w = await ChurchTownAsync(); var f = w.F; var peer = w.Peer;
            var (_, room) = await PreparedAsync(w);
            var guest = AddCharacter(f, 3, 73, job: 0, gold: 100_000);
            var key = await InviteAsync(f, f.Session, guest);
            await PaidGuestAsync(f, guest, key, 10_000, room);
            Arrive(f, guest, room);
            Assert.Equal(0, await InTx(f, () => Venue(f).PostGuestbookAsync(guest, key, "hi"u8.ToArray(), default)));
            var written = Assert.Single((await InTx(f, () => Venue(f).ReadGuestbookAsync(f.Session, 72, default))).Comments);

            var impostor = Impostor(f, f.Session, 9);
            Assert.Equal(-1, (await InTx(f, () => Venue(f).ReadGuestbookAsync(impostor, 72, default))).Status);
            Assert.Equal(-1, await InTx(f, () => Venue(f).DeleteGuestbookAsync(impostor, 72, written.Id, default)));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_guest_comments"));
            Assert.Single((await InTx(f, () => Venue(f).ReadGuestbookAsync(peer, 71, default))).Comments);
        });
    }

    [Fact]
    public async Task B1_4_a_closed_relation_is_no_pair_at_any_entry_where_the_view_used_to_show_it_as_closed()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var w = await ChurchTownAsync(Tier0()); var f = w.F; var peer = w.Peer;
            await EligibleAsync(w, f.Session, peer);
            // Participants stay active: the only way a stored Closed relation can still be found by character.
            f.Sql("UPDATE marriage_relations SET stage=5");

            Assert.Null(await Query(f).ViewAsync(f.Session, default));
            Assert.Same(MarriageEngagementSnapshot.Empty, await Query(f).SnapshotAsync(f.Session.AccountId, f.Session.CharacterId, default));
            Assert.Equal(new[] { f.Session.AccountId }, await Query(f).CoupleAccountsAsync(f.Session, default));
            Assert.Equal(-3, (await InTx(f, () => TestEffectFootprints.Run(f.Session, cap => Couple(f).UseLovePointAsync(f.Session,
                new BusinessSlot(BusinessInventorySpace.Consumable, 0), [f.Session.AccountId], cap, default)))).Status);
            Assert.Equal(MarriageCeremonyFailure.Ineligible,
                (await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, peer, 0, 0, default))).Failure);
            Assert.Null(await InTx(f, () => Venue(f).PrepareChurchAsync(f.Session, default)));
            Assert.Equal(-1, (await InTx(f, () => Venue(f).ReadGuestbookAsync(f.Session, 72, default))).Status);
            Assert.Empty(await WeddingQuests(f).EligibleQuestIdsAsync(f.Session.AccountId, f.Session.CharacterId, default));
            Assert.False(await WeddingQuests(f).CanAcceptAsync(f.Session.AccountId, f.Session.CharacterId, 20737, default));
        });
    }

    [Fact]
    public async Task B1_5_the_view_reports_trials_not_cleared_when_quest_content_is_missing_where_it_used_to_throw()
    {
        var content = new SwitchableContent();
        await using var w = await ChurchTownAsync(Tier0(), s => s.AddSingleton<IMarriageProposalContentSource>(content));
        var f = w.F; var peer = w.Peer;
        await EligibleAsync(w, f.Session, peer);
        Assert.True((await Query(f).ViewAsync(f.Session, default))!.TrialsCleared);

        content.Missing = true;
        var view = (await Query(f).ViewAsync(f.Session, default))!;
        Assert.Equal((MarriageStage.Engaged, false), (view.Stage, view.TrialsCleared));
    }
}
