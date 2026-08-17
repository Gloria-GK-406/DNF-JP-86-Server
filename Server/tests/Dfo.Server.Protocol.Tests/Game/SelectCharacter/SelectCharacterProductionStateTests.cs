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
    public async Task ApplicationProducedResult_EncodesTheCompleteStaticStream()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        var sources = new Sources(seed);
        var handler = new SelectCharacterPreparationService(
            sources, sources, sources, sources, sources, sources, sources, sources,
            sources, sources, sources, sources, sources, sources, sources, sources,
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
        Assert.Equal(57, messages.Count); // Knight-only row 10 is the sole skipped static row.
        Assert.False(sources.Published);
    }

    private sealed class Sources(SelectCharacterResult seed) :
        ICharacterService, ICharacterInventoryEntryService, IAccountInventoryEntryService,
        ICharacterEntryStateService, IAccountEntryStateService, IEquippedCreatureQuery,
        IWorldContentEntryService, ISkillPointDefinitionSource, IChannelEntryPolicy,
        ICharacterPvpSkillProfileRepository, IDungeonPersistentEffectRecoveryRepository,
        IExpertSkillContractDefinitionSource, IExpertSkillContractReconciliationRepository,
        ICreatureEquipmentLifetimeSource, IEntryPlacementService,
        ISessionAdjacentEntryService, IActiveCharacterService
    {
        public bool Published { get; private set; }
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(AccountId id, CancellationToken ct) =>
            ValueTask.FromResult(new AccountCharacterRoster(id, 24,
                [new CharacterSummary(seed.Character.Identity.CharacterId,
                    seed.Character.Identity.SlotIndex, seed.Character.Identity.Name,
                    seed.Character.Identity.Job, seed.Character.Identity.GrowType,
                    seed.Character.Progression.Level, seed.CharacterEntry.Public.CloneTitleItemId)]));
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
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryService.LoadAsync(
            AccountId id, CharacterId cid, CancellationToken ct) => ValueTask.FromResult(seed.CharacterInventory);
        ValueTask<AccountInventoryEntryState> IAccountInventoryEntryService.LoadAsync(
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
        ValueTask<AccountEntryState> IAccountEntryStateService.LoadAsync(AccountId id, CancellationToken ct) =>
            ValueTask.FromResult(seed.Account);
        ValueTask<AccountSettingsEntryState> IAccountEntryStateService.LoadSettingsAsync(
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
        public ValueTask<DungeonPersistentEffectRecoveryOutcome> RecoverAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DungeonPersistentEffectRecoveryOutcome(0, 0, 0, 0));
        public IReadOnlySet<int> GetOverSkillPremiumTypes() => new HashSet<int>();
        public ValueTask<bool> ReconcileAsync(AccountId accountId, CharacterId characterId,
            IReadOnlySet<int> premiumTypes, IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
            IReadOnlyDictionary<int, byte> freeBaseline, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
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
        public ValueTask<SessionAdjacentEntryState> LoadAsync(SessionAdjacentSubject subject, CancellationToken ct) =>
            ValueTask.FromResult(SessionAdjacentEntryState.Empty);
        public void Publish(AccountId id, ActiveCharacterState character) => Published = true;
        public void Publish(SelectedSessionIdentity session, ActiveCharacterState character) => Published = true;
        public bool ClearExact(SelectedSessionIdentity session) => Published = false;
        public ActiveCharacterState GetRequired(AccountId id) => seed.Character;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
