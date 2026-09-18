using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

public sealed class SelectCharacterProductionStateTests
{
    [Fact]
    public async Task CurrentView_ReadsChangedDurabilityWithoutRepeatingEntryRecovery()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        var sources = new Sources(seed, []);
        var service = new SelectCharacterPreparationService(
            sources, sources, sources, sources, sources, sources, sources,
            new CharacterEntryPreparationRuleService(sources, sources, sources),
            sources, sources, sources, sources, sources, sources, sources,
            new FixedTimeProvider(seed.World.PreparedAt));
        var session = SelectedSessionIdentity.Create(
            seed.Account.AccountId, seed.Character.Identity.CharacterId,
            new Dfo.Server.Data.Networking.NetworkConnectionId("current-view"), 1, 10011);
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

        var current = await service.LoadCurrentAsync(session, CancellationToken.None);

        Assert.Equal(37, current.CharacterInventory.Containers.Single(container =>
            container.ContainerType == InventoryContainerType.Equipment).Items.Single().Core.Durability);
        Assert.Equal(0, sources.RecoveryCalls);
        Assert.Equal(0, sources.ReconciliationCalls);
    }

    [Fact]
    public async Task ApplicationProducedResult_EncodesTheCompleteStaticStream()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        var sources = new Sources(seed, []);
        var handler = new SelectCharacterPreparationService(
            sources, sources, sources, sources, sources, sources, sources,
            new CharacterEntryPreparationRuleService(sources, sources, sources),
            sources, sources, sources, sources, sources, sources, sources,
            new FixedTimeProvider(seed.World.PreparedAt));
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
        Assert.False(sources.Published);
    }

    /// <summary>
    /// The roster frame in the entry burst is drawn from what is worn, not from the stored blob.
    /// </summary>
    /// <remarks>
    /// The two answers are made to differ on purpose: the character wears a coat in slot twelve and
    /// the stored blob says slot zero. A frame built from the blob would name slot zero, so this
    /// case cannot pass against the code as it was.
    /// <para>
    /// <b>The worn piece goes through <see cref="DrawnAppearance"/> rather than being written out as
    /// a finished part, and the expected values are written out instead.</b> That is what puts the
    /// shared projection inside this case: changing how a worn piece becomes a drawn part moves what
    /// this frame carries, and this goes red. Handing the listing a hand-made part would have
    /// stubbed the projection out and left the in-world frame as the only side that could feel it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_roster_frame_is_drawn_from_what_is_worn_and_not_from_the_stored_blob()
    {
        var blobSaysSlotZero = new byte[]
        {
            1,
            0x00, 0x40, 0x4C, 0xA9, 0x18, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        var seed = WithStoredAppearance(
            SelectCharacterPayloadTestData.CreateComplete(), blobSaysSlotZero);

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
        var blobSaysSlotZero = new byte[]
        {
            1,
            0x00, 0x40, 0x4C, 0xA9, 0x18, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        var seed = WithStoredAppearance(
            SelectCharacterPayloadTestData.CreateComplete(), blobSaysSlotZero);

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

    private static SelectCharacterResult WithStoredAppearance(
        SelectCharacterResult seed, byte[] blob) => seed with
        {
            CharacterEntry = seed.CharacterEntry with
            {
                Public = seed.CharacterEntry.Public with
                {
                    Appearance = new CharacterAppearanceCompatibilityState(1, blob),
                },
            },
        };

    private static async Task<SelectCharacterResult> PrepareAsync(
        Sources sources, SelectCharacterResult seed)
    {
        var handler = new SelectCharacterPreparationService(
            sources, sources, sources, sources, sources, sources, sources,
            new CharacterEntryPreparationRuleService(sources, sources, sources),
            sources, sources, sources, sources, sources, sources, sources,
            new FixedTimeProvider(seed.World.PreparedAt));
        var channel = new ChannelEntryIdentity(1, 10011, ChannelEntryKind.General);
        var selected = await handler.PreflightAsync(
            seed.Account.AccountId, seed.Character.Identity.SlotIndex, channel, CancellationToken.None);
        return await handler.PrepareAsync(
            selected, seed.Account.AccountId, channel, CancellationToken.None);
    }

    private sealed class Sources(
        SelectCharacterResult seed,
        IReadOnlyList<CharacterRosterAppearanceState>? appearances = null) :
        ICharacterDomainService, ICharacterInventoryEntryDomainService, IAccountInventoryEntryDomainService,
        ICharacterEntryStateDomainService, IAccountEntryStateDomainService, IEquippedCreatureQuery,
        IWorldContentEntryRuleService, ISkillPointDefinitionSource, IChannelEntryPolicy,
        ICharacterPvpSkillProfileRepository, IDungeonPersistentEffectRecoveryRepository,
        IExpertSkillContractDefinitionSource, IExpertSkillContractReconciliationRepository,
        ICreatureEquipmentLifetimeSource, IEntryPlacementRuleService,
        ISessionAdjacentEntryDomainService, IActiveCharacterDomainService,
        ISelectedSessionPositionPersistence
    {
        public bool Published { get; private set; }
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
        // appearance, which is not a convenience: that seed's stored blob is a single zero byte, a
        // declared count of no parts, so the decoder this replaces produced an empty list from it
        // too, and the stream that case is about is the same stream it was about before.
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
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryDomainService.LoadAsync(
            AccountId id, CharacterId cid, CancellationToken ct) => ValueTask.FromResult(CharacterInventory);
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
            ChannelEntryIdentity channel, int town, CancellationToken ct) => ValueTask.FromResult(seed.World.Channel);
        public ValueTask<bool> CanEnterTownAsync(ChannelEntryIdentity channel, int town, CancellationToken ct) =>
            ValueTask.FromResult(true);
        public ValueTask<bool> DeclaresAreaAsync(int town, int area, CancellationToken ct) =>
            ValueTask.FromResult(true);
        public ValueTask<bool> SharesTownPresenceAsync(int town, int area, CancellationToken ct) =>
            ValueTask.FromResult(true);
        public ValueTask<SessionAdjacentEntryState> LoadAsync(SessionAdjacentSubject subject, CancellationToken ct) =>
            ValueTask.FromResult(SessionAdjacentEntryState.Empty);
        public void Publish(SelectedSessionIdentity session) => Published = true;
        public bool ClearExact(SelectedSessionIdentity session) => Published = false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
