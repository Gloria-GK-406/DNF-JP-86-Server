using Dfo.Server.Application.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

public sealed class SelectCharacterProductionStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_and_current_view_use_authoritative_achievement_projection_instead_of_legacy_task_rows(bool login)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var seed = SelectCharacterPayloadTestData.CreateComplete();
            seed = seed with { CharacterEntry = seed.CharacterEntry with { Features = seed.CharacterEntry.Features with
            { Collections = seed.CharacterEntry.Features.Collections with { Achievements = [new(4303, 0, 0, 0, 0)] } } } };
            var sources = new Sources(seed, []);
            var achievements = new ProjectionAchievements(login);
            SelectCharacterResult result;
            if (login) result = await PrepareAsync(sources, seed, achievements);
            else
            {
                var session = SelectedSessionIdentity.Create(seed.Account.AccountId, seed.Character.Identity.CharacterId,
                    new Dfo.Server.Data.Networking.NetworkConnectionId("achievement-read"), 1, 10011);
                var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
                positions.Begin(session, seed.Character.Position, 100);
                result = await Query(sources, seed, positions, achievements).ReadAsync(session, default);
            }
            Assert.Equal(6501, Assert.Single(result.CharacterEntry.Features.Collections.Achievements).AchievementId);
            Assert.Equal(1, achievements.Reads);
            Assert.Equal(login ? 2 : 0, achievements.Writes);
            Assert.DoesNotContain(Assert.Single(new SelectedCharacterStateMessageFactoryService().CreateValidated(result)
                .OfType<Dfo.Server.Protocol.Game.StateSynchronization.AchievementStateMessage>()).Achievements, x => x.AchievementId == 4303);
        });
    }

    private sealed class ProjectionAchievements(bool allowPreparation) : IAchievementDomainService
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        private void CheckPreparation() { Assert.True(allowPreparation); Writes++; }
        public ValueTask PrepareAsync(CharacterId id, int level, CancellationToken ct) { CheckPreparation(); return ValueTask.CompletedTask; }
        public ValueTask<IReadOnlyList<int>> UnlockSatisfiedAchievementsAsync(CharacterId id, CancellationToken ct)
        { CheckPreparation(); return ValueTask.FromResult<IReadOnlyList<int>>([]); }
        public ValueTask<IReadOnlyList<CharacterAchievementProgressState>> GetAsync(CharacterId id, CancellationToken ct)
        { Reads++; return ValueTask.FromResult<IReadOnlyList<CharacterAchievementProgressState>>([new(6501, 1, 0, 0, 0)]); }
        public ValueTask<CharacterAchievementProgressState?> ReconcileAsync(CharacterId id, int aid, CancellationToken ct) => throw new NotSupportedException();
    }
    [Theory]
    [InlineData(11, 11)]
    [InlineData(0, 12)]
    public async Task Login_uses_the_remembered_inn_and_legacy_saves_use_the_current_town(byte inn, int requestedTown)
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        seed = seed with { Character = seed.Character with
        {
            Position = new(12, 0, 562, 183, 5), LastInnTownId = inn,
        } };
        var sources = new Sources(seed, []);
        await PrepareAsync(sources, seed);
        Assert.Equal(requestedTown, sources.RequestedLoginTown);
        Assert.Equal(1, sources.PlacementCalls);
    }

    private sealed class QueryOnlyEpicBook : Dfo.Server.Abstractions.Inventory.IEpicBookDomainService
    {
        public Dfo.Server.Data.Inventory.EpicPieceDefinition? FindPiece(int id) => null;
        public ValueTask<Dfo.Server.Data.Inventory.EpicPieceAddition> AddPiecesAsync(Dfo.Server.Data.Accounts.AccountId account, int pieceId, int count, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<Dfo.Server.Data.Inventory.EpicPieceBalance>> GetBalancesAsync(Dfo.Server.Data.Accounts.AccountId account, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<Dfo.Server.Data.Inventory.EpicPieceBalance>>([new(10300000, 13)]);
        public ValueTask<IReadOnlyList<Dfo.Server.Data.Inventory.EpicBookEntry>> GetBookAsync(Dfo.Server.Data.Accounts.AccountId account, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask InitializeAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<Dfo.Server.Data.Inventory.EpicPieceBalance?> TrySpendPiecesAsync(Dfo.Server.Data.Accounts.AccountId account, int pieceId, int count, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task CurrentView_ReadsChangedDurabilityWithoutRepeatingEntryRecovery()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var seed = SelectCharacterPayloadTestData.CreateComplete();
            var sources = new Sources(seed, []);
            var session = SelectedSessionIdentity.Create(
                seed.Account.AccountId, seed.Character.Identity.CharacterId,
                new Dfo.Server.Data.Networking.NetworkConnectionId("current-view"), 1, 10011);
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            positions.Begin(session, seed.Character.Position, 100);
            var service = Query(sources, seed, positions);
            var equipment = seed.CharacterInventory.Containers.Single(container =>
                container.ContainerType == InventoryContainerType.Equipment);
            var changed = equipment with
            {
                Items = [equipment.Items.Single() with
                {
                    Core = equipment.Items.Single().Core with { Durability = 37 },
                }],
            };
            sources.CharacterInventory = seed.CharacterInventory with
            {
                Containers = seed.CharacterInventory.Containers
                    .Select(container => container.ContainerType == InventoryContainerType.Equipment
                        ? changed : container).ToArray(),
            };

            var current = await service.ReadAsync(session, CancellationToken.None);

            Assert.Equal(37, current.CharacterInventory.Containers.Single(container =>
                container.ContainerType == InventoryContainerType.Equipment).Items.Single().Core.Durability);
            Assert.Equal(0, sources.RecoveryCalls);
            Assert.Equal(0, sources.ReconciliationCalls);
        });
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 0)]
    public async Task CurrentView_UsesLivePositionWithoutLoginPlacementOrInventoryMaintenance(byte town, byte area)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var seed = SelectCharacterPayloadTestData.CreateComplete();
            var sources = new Sources(seed, []) { RejectPlacement = true };
            var session = SelectedSessionIdentity.Create(
                seed.Account.AccountId, seed.Character.Identity.CharacterId,
                new Dfo.Server.Data.Networking.NetworkConnectionId("time-gate"), 1, 10011);
            var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            var standing = new CharacterPositionState(town, area, 185, 215, 2);
            positions.Begin(session, standing, 100);

            var current = await Query(sources, seed, positions).ReadAsync(session, CancellationToken.None);

            Assert.Equal(standing, current.Character.Position);
            Assert.Equal(town, current.World.Channel.Spawn.TownId);
            Assert.Equal(area, current.World.Channel.Spawn.AreaId);
            Assert.Equal(185, current.World.Channel.Spawn.X);
            Assert.Equal(215, current.World.Channel.Spawn.Y);
            Assert.Equal(2, current.World.Channel.Spawn.Direction);
            Assert.Equal(standing, positions.PositionOf(session.CharacterId));
            Assert.Equal(0, sources.PlacementCalls);
            Assert.Equal(0, sources.InventoryMaintenanceCalls);
            Assert.Equal(0, sources.RecoveryCalls);
            Assert.Equal(0, sources.ReconciliationCalls);
            Assert.Equal((town, area), sources.AdjacentPosition);
        });
    }

    [Fact]
    public async Task CurrentView_RequiresLivePositionInsteadOfInventingALoginSpawn()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var seed = SelectCharacterPayloadTestData.CreateComplete();
            var sources = new Sources(seed, []) { RejectPlacement = true };
            var session = SelectedSessionIdentity.Create(
                seed.Account.AccountId, seed.Character.Identity.CharacterId,
                new Dfo.Server.Data.Networking.NetworkConnectionId("missing-position"), 1, 10011);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await Query(sources, seed, new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService())).ReadAsync(session, CancellationToken.None));
            Assert.Equal(0, sources.PlacementCalls);
        });
    }

    private static CharacterCurrentStateQueryDomainService Query(
        Sources sources, SelectCharacterResult seed, IOnlineCharacterRuntimeDomainService positions, IAchievementDomainService? achievements = null) =>
        new(sources, sources, sources, sources, sources, new Dfo.Server.Application.Characters.CharacterAppearanceRuleService(), sources, sources,
            new CharacterEntryPreparationRuleService(sources, sources, sources),
            sources, sources, sources, sources, positions, new FixedTimeProvider(seed.World.PreparedAt), achievements: achievements);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 900)]
    [InlineData(true, 900)]
    public async Task Equipped_creature_can_reenter_and_encode_the_complete_login_stream(
        bool refill, int remainingSeconds)
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        var pet = new InventoryItemState(new ItemIdentity(948), 25,
            StackableItemCore.Create(5, 63000, 948) with
            {
                ExpireTime = remainingSeconds == 0 ? 0
                    : checked((int)seed.World.PreparedAt.ToUnixTimeSeconds() + remainingSeconds),
            });
        seed = seed with
        {
            CharacterInventory = seed.CharacterInventory with
            {
                Containers = seed.CharacterInventory.Containers.Select(container =>
                    container.ContainerType == InventoryContainerType.Equipment
                        ? container with { Items = [.. container.Items, pet] } : container).ToArray(),
            },
            CharacterEntry = seed.CharacterEntry with
            {
                Features = seed.CharacterEntry.Features with
                {
                    Pets = [new CharacterPetRuntimeState(948, 100, 0, 0, 0, 0, 1, [], 0)],
                },
            },
        };
        var sources = new Sources(seed, []);
        var result = await PrepareAsync(sources, seed);
        var refills = new EntryRefill(sources, refill);
        var inventoryEntry = new SelectCharacterEntryInventoryDomainService(
            new RefillConfiguration(refills), refills, sources,
            new CharacterContainerDomainService(), null!,
            new DomainTransactionScope(), null!, null!,
            new CharacterEntryPreparationRuleService(sources, sources, sources),
            transientStores: new Dfo.Server.Persistence.Inventory.TransientItemDomainStoreFactory());
        var session = SelectedSessionIdentity.Create(
            seed.Account.AccountId, seed.Character.Identity.CharacterId,
            new Dfo.Server.Data.Networking.NetworkConnectionId("creature-entry"), 1, 10011);
        result = await TestGameplay.RunAsync(async () => await inventoryEntry.PrepareLeaseAsync(session, result, CancellationToken.None));
        result = (await inventoryEntry.ApplyDailyRefillAsync(
            session, result, CancellationToken.None)).Result;

        var messages = new SelectedCharacterStateMessageFactoryService().CreateValidated(result);

        Assert.NotEmpty(messages);
        var worn = result.CharacterInventory.Containers.Single(container =>
            container.ContainerType == InventoryContainerType.Equipment).Items.Single(item => item.SlotIndex == 25);
        Assert.Equal((uint)remainingSeconds, worn.Noti2CreatureMarker);
        Assert.Equal(948, worn.Core.Value);
        Assert.Equal(100, Assert.Single(result.CharacterEntry.Features.Pets).Field04);
    }

    [Fact]
    public async Task ApplicationProducedResult_EncodesTheCompleteStaticStream()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        var sources = new Sources(seed, []);
        var handler = new SelectCharacterPreparationService(
            sources, sources, sources, sources, sources, new Dfo.Server.Application.Characters.CharacterAppearanceRuleService(), sources, sources,
            new CharacterEntryPreparationRuleService(sources, sources, sources),
            sources, sources, sources, sources, sources, sources, sources,
            new FixedTimeProvider(seed.World.PreparedAt), epicBook: new QueryOnlyEpicBook());
        var channel = new ChannelEntryIdentity(1, 10011, ChannelEntryKind.General);

        var selected = await handler.PreflightAsync(seed.Account.AccountId,
            seed.Character.Identity.SlotIndex, channel, CancellationToken.None);
        var result = await handler.PrepareAsync(
            selected, seed.Account.AccountId, channel, CancellationToken.None);
        var messages = new SelectedCharacterStateMessageFactoryService().CreateValidated(result);

        Assert.NotNull(result.CharacterEntry.Skills.ProtocolMirror);
        Assert.All(result.CharacterEntry.Features.Collections.CollectionBoxes,
            value => Assert.NotNull(value.Protocol));
        // 62 static rows on A21, less the knight-only row this job does not take.
        Assert.Equal(61, messages.Count);
        Assert.Equal(13, Assert.Single(result.EpicPieces!).Count);
        Assert.Single(messages.OfType<Dfo.Server.Protocol.Game.StateSynchronization.EpicBookStateMessage>());
        Assert.False(sources.Published);
    }

    /// <summary>
    /// The roster frame in the entry burst is drawn from what is worn.
    /// </summary>
    /// <remarks>
    /// <b>The worn piece goes through <see cref="DrawnAppearance"/> rather than being written out as
    /// a finished part, and the expected values are written out instead.</b> That is what puts the
    /// shared projection inside this case: changing how a worn piece becomes a drawn part moves what
    /// this frame carries, and this goes red. Handing the listing a hand-made part would have
    /// stubbed the projection out and left the in-world frame as the only side that could feel it.
    /// </remarks>
    [Fact]
    public async Task The_roster_frame_is_drawn_from_what_is_worn()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();

        var result = await PrepareAsync(
            new Sources(seed, DrawnAppearance.Of(WornCoatInSlotTwelve())), seed);

        Assert.NotNull(result.RosterEntry);
        var member = Assert.Single(result.RosterEntry.Characters);
        var part = Assert.Single(member.Appearances);
        Assert.Equal(12, part.Slot);
        Assert.Equal(27600, part.DisplayItemId);
        Assert.Equal(4, part.ExpansionLength);
        Assert.Equal(11, part.State);
        Assert.Equal(9, part.Flag20);
    }

    /// <summary>One coat in slot twelve, attribute five, amplified, nine upgrades.</summary>
    /// <remarks>
    /// The state byte the projection makes of that is the attribute's low five bits doubled plus one
    /// for being amplified, which is eleven; the case above pins that rather than recomputing it.
    /// </remarks>
    private static IReadOnlyList<InventoryItemState> WornCoatInSlotTwelve() =>
    [
        new InventoryItemState(
            new ItemIdentity(1),
            12,
            new InventoryItemCoreState(
                1, 27600, 1, 5, 50, 0, 0, 9, 2, 0, -1,
                new ChronicleOptionState(0, 0, 0, 0, 0),
                new ChronicleOptionState(0, 0, 0, 0, 0),
                0, 0, 0, 0, 0,
                new RandomOptionState(0, 0, 0),
                new RandomOptionState(0, 0, 0),
                new RandomOptionState(0, 0, 0),
                0, 0xFF, 0, new RandomOptionState(0, 0, 0),
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
    ];

    /// <summary>A character wearing nothing is drawn wearing nothing, not wearing what it was made in.</summary>
    [Fact]
    public async Task A_character_wearing_nothing_is_drawn_wearing_nothing()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();

        var result = await PrepareAsync(new Sources(seed, []), seed);

        Assert.NotNull(result.RosterEntry);
        Assert.Empty(Assert.Single(result.RosterEntry.Characters).Appearances);
    }

    /// <summary>
    /// A listing that worked nothing out is refused rather than drawn as a character wearing nothing.
    /// </summary>
    /// <remarks>
    /// A null appearance means "this listing did not work it out", which is true of the cheap
    /// listing and must never be true of the one this frame asks. Drawing it as an empty appearance
    /// would undress every character on the screen and read as missing content rather than as the
    /// wiring mistake it would be.
    /// </remarks>
    [Fact]
    public async Task A_listing_that_worked_nothing_out_is_refused()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();

        var refusal = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await PrepareAsync(new Sources(seed, appearances: null), seed));

        Assert.Equal(
            "The account roster this frame is drawn from did not work out appearances.",
            refusal.Message);
    }

    private static async Task<SelectCharacterResult> PrepareAsync(
        Sources sources, SelectCharacterResult seed, IAchievementDomainService? achievements = null)
    {
        var handler = new SelectCharacterPreparationService(
            sources, sources, sources, sources, sources, new Dfo.Server.Application.Characters.CharacterAppearanceRuleService(), sources, sources,
            new CharacterEntryPreparationRuleService(sources, sources, sources),
            sources, sources, sources, sources, sources, sources, sources,
            new FixedTimeProvider(seed.World.PreparedAt), achievements: achievements);
        var channel = new ChannelEntryIdentity(1, 10011, ChannelEntryKind.General);
        var selected = await handler.PreflightAsync(
            seed.Account.AccountId, seed.Character.Identity.SlotIndex, channel, CancellationToken.None);
        return await handler.PrepareAsync(
            selected, seed.Account.AccountId, channel, CancellationToken.None);
    }

    private sealed class Sources(
        SelectCharacterResult seed,
        IReadOnlyList<CharacterRosterAppearanceState>? appearances = null) :
        ICharacterDomainService, ICharacterRosterDomainService, ICharacterInventoryEntryDomainService, IAccountInventoryEntryDomainService,
        ICharacterEntryStateDomainService, IAccountEntryStateDomainService, IEquippedCreatureQuery,
        IWorldContentEntryRuleService, ISkillPointDefinitionSource, IChannelEntryDomainService,
        ICharacterPvpSkillProfileRepository, IDungeonPersistentEffectRecoveryRepository,
        IExpertSkillContractDefinitionSource, IExpertSkillContractReconciliationRepository,
        ICreatureEquipmentLifetimeSource, IEntryPlacementRuleService,
        ISessionAdjacentEntryDomainService,
        ISelectedSessionPositionPersistence, ITownMovementDomainService
    {
        public ValueTask<Dfo.Server.Data.Channel.ChannelLoginEntry> ResolveLoginAsync(int listenerPort,CancellationToken ct) =>
            ValueTask.FromException<Dfo.Server.Data.Channel.ChannelLoginEntry>(new NotSupportedException("This channel-identity fixture does not supply login deployment facts."));

        public bool Published { get; private set; }
        public int RequestedLoginTown { get; private set; }
        public bool RejectPlacement { get; init; }
        public int PlacementCalls { get; private set; }
        public int InventoryMaintenanceCalls { get; private set; }
        public (int Town, int Area) AdjacentPosition { get; private set; }
        public CharacterInventoryEntryState CharacterInventory { get; set; } = seed.CharacterInventory;
        public int RecoveryCalls { get; private set; }
        public int ReconciliationCalls { get; private set; }

        public ValueTask PersistAsync(
            AccountId accountId, CharacterId characterId, CharacterPositionState position,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ICharacter Get(CharacterId characterId) =>
            throw new NotSupportedException("This stand-in answers no questions about a character.");
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(AccountId id, CancellationToken ct) =>
            ValueTask.FromResult(new AccountCharacterRoster(id, 24,
                [new CharacterSummary(seed.Character.Identity.CharacterId,
                    seed.Character.Identity.SlotIndex, seed.Character.Identity.Name,
                    seed.Character.Identity.Job, seed.Character.Identity.GrowType,
                    seed.Character.Progression.Level, seed.CharacterEntry.Public.CloneTitleItemId,
                    null)]));
        // The entry burst draws the roster now, so this answers rather than refusing. It used to
        // refuse, and the sentence it refused with -- that nothing on that path draws a character --
        // is what the change this stand-in serves falsified.
        //
        // What it answers is the case's to decide. The complete-stream case passes an empty
        // appearance, so the stream that case is about draws a character wearing nothing.
        public ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(
            AccountId id, CancellationToken ct) =>
            ValueTask.FromResult(new AccountCharacterRoster(id, 24,
                [new CharacterSummary(seed.Character.Identity.CharacterId,
                    seed.Character.Identity.SlotIndex, seed.Character.Identity.Name,
                    seed.Character.Identity.Job, seed.Character.Identity.GrowType,
                    seed.Character.Progression.Level, seed.CharacterEntry.Public.CloneTitleItemId,
                    appearances)]));
        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(AccountId id, ushort slot, CancellationToken ct) =>
            ValueTask.FromResult(seed.Character);
        public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(
            AccountId id, ushort rosterIndex, CancellationToken ct) =>
            ValueTask.FromResult(seed.Character);
        // Roster mutation plays no part in selecting an already existing character.
        public ValueTask<CharacterNameStatus> CheckNameAsync(byte[] name, CancellationToken ct) =>
            throw new NotSupportedException();
        public ValueTask<CharacterCreationResult> CreateCharacterAsync(
            AccountId id, byte job, byte[] name, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(
            AccountId id, ushort slot, byte[] name, CancellationToken ct) => throw new NotSupportedException();
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryDomainService.ReadCurrentAsync(
            AccountId id, CharacterId cid, CancellationToken ct) => ValueTask.FromResult(CharacterInventory);
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryDomainService.LoadAsync(
            AccountId id, CharacterId cid, CancellationToken ct)
        {
            InventoryMaintenanceCalls++;
            return ValueTask.FromResult(CharacterInventory);
        }
        ValueTask<AccountInventoryEntryState> IAccountInventoryEntryDomainService.LoadAsync(
            AccountId id, CharacterId cid, CancellationToken ct) => ValueTask.FromResult(seed.AccountInventory);
        public ValueTask<CharacterEntryState> LoadAsync(AccountId id, CharacterId cid, CancellationToken ct) =>
            ValueTask.FromResult(seed.CharacterEntry with
            {
                Skills = seed.CharacterEntry.Skills with { ProtocolMirror = null },
                Features = seed.CharacterEntry.Features with
                {
                    Collections = seed.CharacterEntry.Features.Collections with
                    {
                        CollectionBoxes = seed.CharacterEntry.Features.Collections.CollectionBoxes
                            .Select(value => value with { Protocol = null }).ToArray(),
                    },
                },
            });
        ValueTask<AccountEntryState> IAccountEntryStateDomainService.LoadAsync(AccountId id, CancellationToken ct) =>
            ValueTask.FromResult(seed.Account);
        ValueTask<AccountSettingsEntryState> IAccountEntryStateDomainService.LoadSettingsAsync(
            AccountId id, CancellationToken ct) => ValueTask.FromResult(seed.Account.Settings);
        public int GetItemId(CharacterInventoryEntryState inventory) => 0;
        public ValueTask<WorldContentEntryState> LoadAsync(WorldContentSubject subject, CancellationToken ct) =>
            ValueTask.FromResult(seed.World.Content with
            {
                CollectionBoxes =
                [
                    new CollectionBoxEntryState(3, 1, 1, [], null),
                    new CollectionBoxEntryState(4, 1, 1, [], seed.World.PreparedAt.AddSeconds(-1)),
                ],
            });
        public ValueTask<SkillPointDefinitionSet> GetAsync(byte job, byte grow, byte level,
            IReadOnlyCollection<int> ids, CancellationToken ct) => ValueTask.FromResult(
                new SkillPointDefinitionSet(200, 20, [], ids.ToDictionary(id => id,
                    id => new SkillPointCostDefinition(id, false, [10], false, 1, 100, 0, 1, 1))));

        ValueTask<CharacterSkillsEntryState> ICharacterPvpSkillProfileRepository.LoadOrInitializeAsync(
            AccountId accountId, CharacterId characterId, CharacterSkillsEntryState initialProfile,
            CancellationToken cancellationToken) => ValueTask.FromResult(seed.CharacterEntry.Skills);
        ValueTask<CharacterSkillsEntryState> ICharacterPvpSkillProfileRepository.ReadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(seed.CharacterEntry.Skills);
        public ValueTask<DungeonPersistentEffectRecoveryOutcome> RecoverAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
        {
            RecoveryCalls++;
            return ValueTask.FromResult(new DungeonPersistentEffectRecoveryOutcome(0, 0, 0, 0));
        }
        public IReadOnlySet<int> GetOverSkillPremiumTypes() => new HashSet<int>();
        public ValueTask<bool> ReconcileAsync(AccountId accountId, CharacterId characterId,
            IReadOnlySet<int> premiumTypes, IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
            IReadOnlyDictionary<int, byte> freeBaseline, CancellationToken cancellationToken)
        {
            ReconciliationCalls++;
            return ValueTask.FromResult(false);
        }
        public ValueTask<CreatureEquipmentLifetime> GetAsync(
            int itemTemplateId, DateTimeOffset preparedAt, CancellationToken ct) =>
            ValueTask.FromResult(default(CreatureEquipmentLifetime));
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int port, CancellationToken ct) =>
            ValueTask.FromResult(new ChannelEntryIdentity(1, port, ChannelEntryKind.General));
        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int level) => null;
        public ValueTask<ChannelEntryPolicyState> ResolveAsync(
            ChannelEntryIdentity channel, int town, CancellationToken ct)
        {
            PlacementCalls++;
            RequestedLoginTown = town;
            if (RejectPlacement) throw new InvalidOperationException("An online query must not resolve login placement.");
            return ValueTask.FromResult(seed.World.Channel);
        }
        public ValueTask<bool> CanEnterTownAsync(ChannelEntryIdentity channel, int town, CancellationToken ct) =>
            ValueTask.FromResult(true);
        public ValueTask<bool> CanRememberInnAsync(int town, CancellationToken ct) =>
            ValueTask.FromResult(town > 0 && town != 17);
        public ValueTask<bool> DeclaresAreaAsync(int town, int area, CancellationToken ct) =>
            ValueTask.FromResult(true);
        public ValueTask<bool> SharesTownPresenceAsync(int town, int area, CancellationToken ct) =>
            ValueTask.FromResult(true);
        public bool IsHouseholdTown(int townId) => Dfo.Server.Application.World.CoupleRoomRules.IsHouseholdTown(townId);
        public int? AreaInstance(int townId, int? householdInstance) =>
            Dfo.Server.Application.World.CoupleRoomRules.AreaInstance(townId, householdInstance);
        public Dfo.Server.Abstractions.World.TownCrossingRefusal? RefuseCrossing(Dfo.Server.Abstractions.World.TownCrossingFacts facts) =>
            Dfo.Server.Application.World.CoupleRoomRules.RefuseCrossing(facts);
        public Dfo.Server.Data.Characters.CharacterPositionState StoredPositionAfter(
            Dfo.Server.Data.Characters.CharacterPositionState standing, Dfo.Server.Data.Characters.CharacterPositionState destination) =>
            Dfo.Server.Application.World.CoupleRoomRules.StoredPositionAfter(standing, destination);
        public Dfo.Server.Abstractions.World.TownHousingChange HousingChangeOf(int fromTownId, int toTownId) =>
            Dfo.Server.Application.World.CoupleRoomRules.HousingChangeOf(fromTownId, toTownId);
        public ValueTask<SessionAdjacentEntryState> LoadAsync(SessionAdjacentSubject subject, CancellationToken ct)
        {
            AdjacentPosition = (subject.TownId, subject.AreaId);
            return ValueTask.FromResult(SessionAdjacentEntryState.Empty);
        }
        public void Publish(SelectedSessionIdentity session) => Published = true;
        public bool ClearExact(SelectedSessionIdentity session) => Published = false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EntryRefill(Sources sources, bool refill)
        : IEntryLifecycleContentSource, IDailyRefillDomainService
    {
        public ValueTask<IReadOnlyList<DailyRefillRule>> GetDailyRefillRulesAsync(
            DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DailyRefillRule>>([]);

        public ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
            AccountId accountId, CharacterId characterId, IReadOnlyList<DailyRefillRule> rules,
            DateTimeOffset preparedAt, CancellationToken cancellationToken)
        {
            if (!refill) return ValueTask.FromResult(new DailyRefillMutationResult([]));
            sources.CharacterInventory = sources.CharacterInventory with
            {
                Containers = sources.CharacterInventory.Containers.Select(container =>
                    container.ContainerType == InventoryContainerType.Main
                        ? container with
                        {
                            Items = [.. container.Items.Where(item => item.SlotIndex != 65),
                                new InventoryItemState(new ItemIdentity(949), 65,
                                    StackableItemCore.Create(4, 9001, 2))],
                        } : container).ToArray(),
            };
            return ValueTask.FromResult(new DailyRefillMutationResult(
                [new DailyRefillCommittedGrant(new(BusinessInventorySpace.Consumable, 0), 9001, 2, 2)]));
        }
    }
    private sealed class RefillConfiguration(IEntryLifecycleContentSource source) : IDailyRefillConfigurationDomainService
    {
        public ValueTask<IReadOnlyList<DailyRefillRule>> EffectiveAsync(DateTimeOffset at, CancellationToken ct) => source.GetDailyRefillRulesAsync(at, ct);
        public ValueTask<DailyRefillList> ListAsync(DateTimeOffset at, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<DailyRefillList> ChangeAsync(DailyRefillChange change, string actor, DateTimeOffset at, CancellationToken ct) => throw new NotSupportedException();
    }

}
