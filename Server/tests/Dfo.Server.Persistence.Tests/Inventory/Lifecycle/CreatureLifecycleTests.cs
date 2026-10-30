using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Creatures;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Inventory.Lifecycle;

public sealed class CreatureLifecycleTests
{
    private static readonly CharacterId Owner = new(7);
    private static readonly ContainerId Worn = ContainerId.ForCharacter(Owner, InventoryContainerType.Equipment);

    [Fact]
    public async Task LaterItemUpdateIsNotOverwrittenByCreatureFacetAtCommit()
    {
        using var db = new Database();
        var tx = new DomainTransaction(); var items = db.Items.SessionIn(tx);
        var reference = await items.PutAsync(Worn, 25, await items.CreateAsync(Pet(), default), default);
        var creatures = db.Creatures.SessionIn(tx);
        var before = (await creatures.ReadCurrentAsync(Owner, default))!;
        await creatures.WriteAsync(reference, before with { Stomach = 40 }, null, default);
        await items.UpdateAsync(reference, x => x with { CreatureDetail = x.CreatureDetail! with { Field04 = 85 } }, default);
        await tx.CommitAsync(default);
        var read = new DomainTransaction();
        var actual = (await db.Creatures.SessionIn(read).ReadCurrentAsync(Owner, default))!.Stomach;
        await read.RollbackAsync();
        Assert.Equal(85, actual);
    }

    private static ItemSnapshot Pet() => new() { ItemId = 63001, Count = 1, LegacyKind = 5,
        Type = ItemTraits.Creature, CreatureDetail = new(73, 0, 301, 8, 9, 4, [0x81,0x40], 12), TailUnknown3 = 91 };

    [Fact]
    public async Task PetHandleDoesNotFollowUidAcrossMovesOrReturnToOriginalSlot()
    {
        using var db = new Database(); var tx = new DomainTransaction(); var items = db.Items.SessionIn(tx);
        var item = await items.PutAsync(Worn, 25, await items.CreateAsync(Pet(), default), default);
        var context = new CreatureInstanceContext(Owner, tx, db.Creatures.SessionIn(tx), null, new Rules(), TimeProvider.System);
        var collection = new CreatureCollection(() => context);
        var pet = (await collection.FindPetAsync(item.ItemUid!.Value.Value, default))!;
        var bag = ContainerId.ForCharacter(Owner, InventoryContainerType.Pet);
        var moved = await items.PutAsync(bag, 0, await items.TakeAsync(item, null, default), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pet.RenameAsync([65], default).AsTask());
        await items.PutAsync(Worn, 25, await items.TakeAsync(moved, null, default), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pet.ReadAsync(default).AsTask());
        var fresh = (await collection.FindPetAsync(item.ItemUid.Value.Value, default))!;
        Assert.Equal([66], (await fresh.RenameAsync([66], default))!.NameBytes);
        await tx.CommitAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.ReadAsync(default).AsTask());
    }

    [Fact]
    public async Task PetUpdatesKeepCurrentOtherItemFieldsAndEvolutionPreservesIdentity()
    {
        using var db = new Database(); var tx = new DomainTransaction(); var items = db.Items.SessionIn(tx);
        var item = await items.PutAsync(Worn, 25, await items.CreateAsync(Pet(), default), default);
        var context = new CreatureInstanceContext(Owner, tx, db.Creatures.SessionIn(tx), null, new Rules(), TimeProvider.System);
        var pet = (await new CreatureCollection(() => context).FindPetAsync(item.ItemUid!.Value.Value, default))!;
        await items.UpdateAsync(item, x => x with { TailUnknown3 = 92,
            CreatureDetail = x.CreatureDetail! with { ProgressValue = 777 } }, default);
        await pet.RenameAsync([67], default);
        var receipt = (await pet.EvolveAsync(default))!;
        Assert.Equal(63002, receipt.Core.ItemId);
        Assert.Equal(item.ItemUid, receipt.Core.ItemUid);
        Assert.Equal(777, receipt.Core.CreatureDetail!.ProgressValue);
        Assert.Equal(92, receipt.Core.TailUnknown3);
        Assert.Equal([67], receipt.Core.CreatureDetail.CreatureText);
        await tx.CommitAsync(default);
    }

    [Fact]
    public async Task HatchKeepsUidAndUnknownFieldsAndOldEggCannotHatchAgain()
    {
        using var db = new Database(); var tx = new DomainTransaction(); var items = db.Items.SessionIn(tx);
        var bag = ContainerId.ForCharacter(Owner, InventoryContainerType.Pet);
        var item = await items.PutAsync(bag, 0, await items.CreateAsync(Pet() with { ItemId = 63000 }, default), default);
        var context = new CreatureInstanceContext(Owner, tx, db.Creatures.SessionIn(tx), null, new Rules(), TimeProvider.System);
        var egg = (await new CreatureCollection(() => context).FindEggAsync(item.ItemUid!.Value.Value, default))!;
        var born = (await egg.BirthAsync(default))!;
        Assert.Equal(item.ItemUid.Value.Value, born.ItemUid);
        Assert.Equal(100, (await born.ReadAsync(default)).Stomach);
        Assert.Null(await egg.BirthAsync(default));
        var after = (await items.GetAsync(bag, 0, default))!;
        Assert.Equal(63001, after.ItemId); Assert.Equal(91, after.Snapshot.TailUnknown3);
        Assert.Equal(123456, after.Snapshot.ExpireTime);
        await tx.CommitAsync(default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EntryRevivalUsesActualUidAndPreservesTheConditionalGuard(bool guardFails)
    {
        using var db = new Database(); var seed = new DomainTransaction(); var items = db.Items.SessionIn(seed);
        var item = await items.PutAsync(Worn, 25, await items.CreateAsync(Pet() with
            { CreatureDetail = Pet().CreatureDetail! with { Field04 = 0 } }, default), default);
        await seed.CommitAsync(default);
        // Legacy imported payloads may carry a value unrelated to the durable UID.
        using (var connection = new SqliteConnection($"Data Source={db.Path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE items SET item_core=@core";
            command.Parameters.AddWithValue("@core", InventoryItemCore.Encode(item.Snapshot.ToCore() with { Value = 4242 }));
            command.ExecuteNonQuery();
        }
        var tx = new DomainTransaction();
        var characters = new Dfo.Server.Persistence.Characters.SqliteCharacterDomainStore(db.Path).SessionIn(tx);
        var owned = new Dfo.Server.Abstractions.Characters.OwnedCharacterId(new(3), Owner);
        characters.Creatures.Write(owned, new(item.ItemUid!.Value.Value, 1) { OnlyWhenAtMostZero = true });
        if (guardFails)
        {
            items = db.Items.SessionIn(tx); var current = (await items.GetAsync(Worn, 25, default))!;
            await items.UpdateAsync(current, x => x with { CreatureDetail = x.CreatureDetail! with { Field04 = 2 } }, default);
            await Assert.ThrowsAsync<InvalidDataException>(() => tx.CommitAsync(default).AsTask());
        }
        else await tx.CommitAsync(default);
        var read = new DomainTransaction();
        var after = (await db.Items.SessionIn(read).GetAsync(Worn, 25, default))!;
        await read.RollbackAsync();
        Assert.Equal(guardFails ? 0 : 1, after.Snapshot.CreatureDetail!.Field04);
        Assert.Equal(4242, after.Snapshot.InstanceValue);
    }

    [Fact]
    public async Task OnlineFractionSurvivesDetailChangesButExternalStomachResetsFractionAndAnchor()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var db = new Database(); var scope = TestRequired.TransactionScope(); var clock = new Clock();
            var session = SelectedSessionIdentity.Create(new(3), Owner, new("Game:creature"), 1, 10021);
            var sessions = new Sessions(session);
            var runtime = new CreatureRuntimeDomainService(db.Creatures, new Rules(), scope, sessions, clock, inventories: TestRequired.Absent<Dfo.Server.Abstractions.Inventory.ICharacterBusinessInventoryDomainService>());
            // Each tick settles as the use case that drives it would.
            Task<IReadOnlyList<CreatureRuntimeChange>> Advance() => TestUseCase.RunAsync(scope, () => runtime.AdvanceAsync(session, default));
            await using (var seed = scope.Begin())
            {
                var items = db.Items.SessionIn(scope.Current!);
                await items.PutAsync(Worn, 25, await items.CreateAsync(Pet() with
                    { CreatureDetail = Pet().CreatureDetail! with { Field04 = 50 } }, default), default);
                await seed.CommitAsync(default);
            }
            runtime.Enter(session); await Advance();
            clock.Advance(180); await Advance();
            await using (var rename = scope.Begin())
            {
                var pet = (await runtime.Of(Owner).CurrentPetAsync(default))!;
                Assert.Equal(50.5, (await pet.ReadAsync(default)).ExactStomach);
                clock.Advance(60);
                await pet.RenameAsync([65], default);
                await rename.CommitAsync(default);
            }
            clock.Advance(120); await Advance();
            await using (var external = scope.Begin())
            {
                var items = db.Items.SessionIn(scope.Current!); var reference = (await items.GetAsync(Worn, 25, default))!;
                Assert.Equal(51, reference.Snapshot.CreatureDetail!.Field04);
                await items.UpdateAsync(reference, x => x with { CreatureDetail = x.CreatureDetail! with { Field04 = 80 } }, default);
                await external.CommitAsync(default);
            }
            clock.Advance(3600); await Advance();
            clock.Advance(180); await Advance();
            await using (var read = scope.Begin())
            {
                var pet = (await runtime.Of(Owner).CurrentPetAsync(default))!;
                Assert.Equal(80.5, (await pet.ReadAsync(default)).ExactStomach);
                await read.CommitAsync(default);
            }
        });
    }

    private sealed class Rules : ICreatureRuleService
    {
        public ValueTask<CreatureRule?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<CreatureRule?>(new(id, id == 63002 ? 21 : 20, 20, true, [100,200,300],
                id == 63001 ? [new(3, null, 21)] : [], id == 63000 ? 63001 : null, id == 63000)
                { OutputTemplateId = id == 63001 ? 63002 : null });
        public ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(int id, CancellationToken ct) => ValueTask.FromResult<CreatureArtifactExperienceRule?>(null);
        public ValueTask<int> BirthExpirationAsync(int id, DateTimeOffset at, CancellationToken ct) => ValueTask.FromResult(123456);
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026,10,3,0,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now += TimeSpan.FromSeconds(seconds);
    }
    private sealed class Sessions(SelectedSessionIdentity session) : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity? Select(SelectedSessionIdentity value) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity value) => throw new NotSupportedException();
        public SelectedSessionIdentity? Find(AccountId id) => session.AccountId == id ? session : null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => session.CharacterId == id ? session : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => session.WireUserId == id ? session : null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => session.ConnectionId == id ? session : null;
    }

    private sealed class Database : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"creature-lifecycle-{Guid.NewGuid():N}.db");
        public SqliteItemDomainStore Items { get; }
        public SqliteCreatureDomainStore Creatures { get; }
        public Database()
        {
            CharacterDatabaseSchema.Ensure($"Data Source={Path}");
            using var connection = new SqliteConnection($"Data Source={Path}"); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO accounts(account_id,m_id) VALUES(3,'pet-test'); INSERT INTO characters(character_id,account_id,name,job,level) VALUES(7,3,'owner',3,60),(8,3,'other',3,60)";
            command.ExecuteNonQuery(); Items = new(Path, contentsEvents: TestRequired.NoContentsEvents); Creatures = new(Path, contentsEvents: TestRequired.NoContentsEvents);
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); File.Delete(Path); }
    }
}
