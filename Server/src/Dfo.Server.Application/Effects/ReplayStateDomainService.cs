using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Effects;

/// <summary>Reads only requested current facts, in the caller's protected transaction; never performs entry work.</summary>
internal sealed class ReplayStateDomainService(IDomainTransactionAccessor transactions,
    SelectedSessionSafety safety, TimeProvider clock,
    ICharacterInventoryEntryDomainService characterInventories,
    ICharacterContainerDomainService containers,
    ICharacterEntryPreparationRuleService preparationRules,
    Dfo.Server.Abstractions.Characters.ICharacterAppearanceRuleService appearanceRules,
    IAccountInventoryEntryDomainService accountInventories,
    ICharacterEntryStateDomainService entries,
    ICharacterDomainService characters,
    ICharacterRosterDomainService rosters,
    ICharacterSkillPointDomainService skillPoints,
    ICharacterSkillStateDomainService skillStates,
    ICreatureDomainService creatures,
    IEquippedCreatureQuery equippedCreatures,
    Dfo.Server.Abstractions.Quests.IGiantDragonCrystalExchangeDomainService giantDragonCrystals,
    ISouthernDaleQuestDomainService southernDaleQuests,
    Dfo.Server.Abstractions.Quests.IQuestAvailabilityRuleService questAvailability,
    ICharacterFatigueDomainService fatigueBalances,
    IItemUsageDomainService itemUsages,
    IPremiumServiceDomainService premiumServices,
    IPremiumServiceUsageDomainService premiumUsages,
    Dfo.Server.Abstractions.Rental.IEquipmentRentalDomainService rentals,
    ICollectionBoxDomainService collectionBoxes,
    Dfo.Server.Abstractions.Marriage.IMarriageCapsuleQueryDomainService coupleCapsules,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService housing,
    Dfo.Server.Abstractions.Marriage.IMarriageQueryDomainService marriages,
    IHerasQuotaDomainService herasQuotas,
    IEpicBookDomainService epicBooks,
    Dfo.Server.Abstractions.Events.IJoustProjectionDomainService jousts,
    IIllusionTowerDomainService towers,
    IDungeonParticipationDomainService dungeonParticipation) : IReplayStateDomainService
{
    public async ValueTask<ReplayStateSnapshot> ReadAsync(SelectedSessionIdentity session,
        ReplayProjectionRequest request, GameMessageMetadata? metadata, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        _ = transactions.Current ?? throw new InvalidOperationException("Replay state requires a transaction snapshot.");
        var recipient = safety.Capture(session, metadata);
        var at = clock.GetUtcNow();
        var needs = request.Needs;
        var appearanceNeeded = needs.HasFlag(ReplayProjectionNeeds.CharacterAppearance);
        ReplayInventoryState? inventory = null;
        CharacterInventoryEntryState? characterInventory = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Inventory) || appearanceNeeded || needs.HasFlag(ReplayProjectionNeeds.Quests))
        {
            characterInventory = await characterInventories
                .ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            if (towers.Find(session) is { UsesTowerInventory: true })
            {
                var tower = containers.GetContainer(session.CharacterId, InventoryContainerType.QuickSlot);
                var items = await tower.GetAllAsync(ct).ConfigureAwait(false);
                characterInventory = characterInventory with { Containers = [.. characterInventory.Containers,
                    new InventoryContainerState(InventoryContainerType.QuickSlot, tower.Shape.Parameter,
                        items.Select(item => new InventoryItemState(item.ItemUid, item.StorageSlotIndex, item.Snapshot.ToCore())).ToArray())] };
            }
            if (appearanceNeeded)
                characterInventory = await SupportCreatureMarkerProjection.CompleteAsync(characterInventory,
                    preparationRules, at, ct).ConfigureAwait(false);
            if (needs.HasFlag(ReplayProjectionNeeds.Inventory) || appearanceNeeded)
            {
                var accountInventory = await accountInventories
                    .LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
                inventory = new(characterInventory, accountInventory);
            }
        }
        CharacterEntryState? entry = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Quests) || needs.HasFlag(ReplayProjectionNeeds.Skills) || appearanceNeeded)
            entry = await entries
                .LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);

        CharacterProgressionState? progress = null;
        CharacterIdentityState? identity = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Experience) || needs.HasFlag(ReplayProjectionNeeds.Quests) || appearanceNeeded)
        {
            var character = characters.Get(session.CharacterId);
            progress = await character.ProgressAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Current character progress is unavailable.");
            if (needs.HasFlag(ReplayProjectionNeeds.Quests) || appearanceNeeded)
                identity = await character.IdentityAsync(ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Current character identity is unavailable.");
        }
        CharacterSkillProtocolMirrorState? points = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Skills) || needs.HasFlag(ReplayProjectionNeeds.Experience) || appearanceNeeded)
            points = (await skillPoints.LedgerAsync(session.CharacterId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Current skill point ledger is unavailable.")).ToProtocolMirror();

        ReplaySkillState? skills = null;
        CharacterSkillBuilds? builds = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Skills) || appearanceNeeded)
        {
            var state = skillStates;
            var facts = await state.ReadAsync(session.CharacterId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Current skills are unavailable.");
            builds = await state.BuildsAsync(session.CharacterId, ct).ConfigureAwait(false);
            skills = new(entry!.Skills with { Pages = facts.Pages, SkillPointBasis = facts.PointBasis, ProtocolMirror = points });
        }
        ImmutableArray<ReplayPetState> pets = [];
        if (needs.HasFlag(ReplayProjectionNeeds.Pets) || appearanceNeeded)
            pets = (await creatures.GetStore(session.CharacterId).ReadAllAsync(ct).ConfigureAwait(false))
                .Select(CreatureRuntimeProjection.Build).Select(ReplayPetState.From).ToImmutableArray();

        ReplayQuestState? quests = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Quests))
        {
            var subject = new WorldContentSubject(progress!.Level, identity!.Job, identity.GrowType,
                entry!.Quests.VisibilityFlags.Select((value, index) => (value, index)).Where(x => x.value != 0)
                    .ToImmutableDictionary(x => x.index, x => (int)x.value),
                entry.Quests.ActiveQuests.Select(x => x.QuestId).ToImmutableArray(),
                entry.Progression.DailyChallenges.Where(x => x.RewardClaimed).Select(x => checked((int)x.GroupId)).ToImmutableArray(),
                entry.Features.Collections.CollectionBoxes.Select(x => new CollectionBoxProgressSubject(x.BoxIndex,
                    x.Slots.Select(y => y.ItemId).ToImmutableArray())).ToImmutableArray(),
                equippedCreatures.GetItemId(characterInventory!), entry.Public.DerivedStats.EquippedCreatureLevel)
            { GiantDragonCrystalExchangeAvailable = await giantDragonCrystals.CanAcceptAsync(session.CharacterId, ct).ConfigureAwait(false),
                SouthernDaleQuestAvailable = await southernDaleQuests.CanAcceptAsync(session.CharacterId, ct).ConfigureAwait(false) };
            var offers = await questAvailability.ReadAvailableAsync(subject, ct).ConfigureAwait(false);
            quests = new(entry.Quests.ActiveQuests.ToImmutableArray(), entry.Quests.VisibilityFlags.ToImmutableArray(), offers.Quests.ToImmutableArray());
        }
        CharacterFatigueBalance? fatigue = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Fatigue) || appearanceNeeded)
            fatigue = await fatigueBalances.BalanceAtAsync(session.CharacterId, at, ct).ConfigureAwait(false);
        ImmutableArray<ItemUsageState> itemUsage = [];
        if (needs.HasFlag(ReplayProjectionNeeds.ItemUsage))
            itemUsage = (await itemUsages.GetSnapshotAtAsync(session.CharacterId, at, ct).ConfigureAwait(false)).ToImmutableArray();
        ImmutableArray<AccountPremiumEntryState> premiums = [];
        ImmutableArray<CharacterPremiumUsageSlotState> premiumUsage = [];
        if (needs.HasFlag(ReplayProjectionNeeds.Premium))
        {
            premiums = (await premiumServices.For(session.AccountId).ReadAtAsync(at, ct).ConfigureAwait(false)).ToImmutableArray();
            premiumUsage = (await premiumUsages.ReadAtAsync(session.CharacterId, at, ct).ConfigureAwait(false)).ToImmutableArray();
        }
        ReplayCharacterState? appearance = null;
        if (appearanceNeeded)
        {
            entry = CharacterStateProjection.CompleteRuntimeIdentityProjection(
                entry!, dungeonParticipation.Find(session.CharacterId) is not null);
            var character = await rosters.LoadForSelectionAsync(session.AccountId, identity!.SlotIndex, ct).ConfigureAwait(false);
            if (character.Identity.CharacterId != session.CharacterId)
                throw new InvalidDataException("Current character selection changed its owner.");
            entry = CharacterStateProjection.CompleteAppearanceProjection(appearanceRules, character with { Identity = identity! }, entry!, characterInventory!);
            appearance = new(character with { Identity = identity, Progression = progress!,
                Fatigue = new(fatigue!.Value.Total, fatigue.Value.Used) { ConsumedToday = fatigue.Value.ConsumedToday } },
                entry!.Public with { Extras = entry.Public.Extras with { CharacterExperience = progress!.Experience,
                    SkillTreeIndex = builds!.IsUnlocked(new(1)) ? builds.Active.Value : -1 } });
        }
        safety.DemandCurrent(recipient);
        return new(session, request, at, inventory, pets, skills, quests, progress, points, fatigue,
            itemUsage, premiums, premiumUsage, appearance)
        { Rental = needs.HasFlag(ReplayProjectionNeeds.Rental) ? await rentals.PanelAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false) : null,
          CollectionBoxes = needs.HasFlag(ReplayProjectionNeeds.CollectionBox) ? await collectionBoxes.ReadAsync(session.CharacterId, ct).ConfigureAwait(false) : null,
          Capsules = needs.HasFlag(ReplayProjectionNeeds.CoupleCapsules) ? await coupleCapsules.ReadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false) : null,
          Housing = (needs.HasFlag(ReplayProjectionNeeds.MarriageHousing) || appearanceNeeded)
            ? await housing.ReadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false) : null,
          MarriageInfo = (needs.HasFlag(ReplayProjectionNeeds.MarriageHousing) || appearanceNeeded)
            ? await housing.InfoAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false) : null,
          LovePoints = needs.HasFlag(ReplayProjectionNeeds.MarriageLovePoints)
            ? (await marriages.SnapshotAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false)).LovePoints : null,
          HerasQuota = needs.HasFlag(ReplayProjectionNeeds.HerasQuota)
            ? await herasQuotas.ReadQuotaAtAsync(session.CharacterId, at, ct).ConfigureAwait(false) : null,
          EpicPieces = needs.HasFlag(ReplayProjectionNeeds.EpicBook)
            ? (await epicBooks.GetBalancesAsync(session.AccountId, ct).ConfigureAwait(false)).ToImmutableArray() : default,
          Joust = needs.HasFlag(ReplayProjectionNeeds.Joust)
            ? await jousts.ReadAsync(session.CharacterId,at,ct).ConfigureAwait(false) : null };
    }
}
