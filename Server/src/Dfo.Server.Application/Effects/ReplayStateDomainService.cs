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
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Effects;

public sealed record ReadReplayStateCommand(SelectedSessionIdentity Session, ReplayProjectionRequest Request, GameMessageMetadata? Metadata)
    : ICommand<ReplayStateSnapshot>, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class ReadReplayStateCommandHandler(IReplayStateDomainService reader)
    : ICommandHandler<ReadReplayStateCommand, ReplayStateSnapshot>
{
    public ValueTask<ReplayStateSnapshot> HandleAsync(CommandExecutionContext context,
        ReadReplayStateCommand command, CancellationToken cancellationToken) =>
        reader.ReadAsync(command.Session, command.Request, command.Metadata, cancellationToken);
}

/// <summary>Reads only requested current facts, in the caller's protected transaction; never performs entry work.</summary>
internal sealed class ReplayStateDomainService(IServiceProvider services, IDomainTransactionAccessor transactions,
    SelectedSessionSafety safety, TimeProvider clock) : IReplayStateDomainService
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
        if (!await Get<ICharacterTable>().BelongsToAsync(session.CharacterId, session.AccountId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Replay state does not belong to the selected account.");
        var needs = request.Needs;
        var appearanceNeeded = needs.HasFlag(ReplayProjectionNeeds.CharacterAppearance);
        ReplayInventoryState? inventory = null;
        CharacterInventoryEntryState? characterInventory = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Inventory) || appearanceNeeded || needs.HasFlag(ReplayProjectionNeeds.Quests))
        {
            characterInventory = await Get<ICharacterInventoryEntryDomainService>()
                .ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            if (services.GetService<IIllusionTowerDomainService>()?.Find(session) is { DungeonId: 11000 })
            {
                var tower = Get<ICharacterContainerDomainService>().GetContainer(session.CharacterId, InventoryContainerType.QuickSlot);
                var items = await tower.GetAllAsync(ct).ConfigureAwait(false);
                characterInventory = characterInventory with { Containers = [.. characterInventory.Containers,
                    new InventoryContainerState(InventoryContainerType.QuickSlot, tower.Shape.Parameter,
                        items.Select(item => new InventoryItemState(item.ItemUid, item.StorageSlotIndex, item.Snapshot.ToCore())).ToArray())] };
            }
            if (appearanceNeeded)
                characterInventory = await SupportCreatureMarkerProjection.CompleteAsync(characterInventory,
                    Get<ICharacterEntryPreparationRuleService>(), at, ct).ConfigureAwait(false);
            if (needs.HasFlag(ReplayProjectionNeeds.Inventory) || appearanceNeeded)
            {
                var accountInventory = await Get<IAccountInventoryEntryDomainService>()
                    .LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
                inventory = new(characterInventory, accountInventory);
            }
        }
        CharacterEntryState? entry = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Quests) || needs.HasFlag(ReplayProjectionNeeds.Skills) || appearanceNeeded)
            entry = await Get<ICharacterEntryStateDomainService>()
                .LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);

        CharacterProgressionState? progress = null;
        CharacterIdentityState? identity = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Experience) || needs.HasFlag(ReplayProjectionNeeds.Quests) || appearanceNeeded)
        {
            var character = Get<ICharacterDomainService>().Get(session.CharacterId);
            progress = await character.ProgressAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Current character progress is unavailable.");
            if (needs.HasFlag(ReplayProjectionNeeds.Quests) || appearanceNeeded)
                identity = await character.IdentityAsync(ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Current character identity is unavailable.");
        }
        CharacterSkillProtocolMirrorState? points = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Skills) || needs.HasFlag(ReplayProjectionNeeds.Experience) || appearanceNeeded)
            points = (await Get<ICharacterSkillPointDomainService>().LedgerAsync(session.CharacterId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Current skill point ledger is unavailable.")).ToProtocolMirror();

        ReplaySkillState? skills = null;
        CharacterSkillBuilds? builds = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Skills) || appearanceNeeded)
        {
            var state = Get<ICharacterSkillStateDomainService>();
            var facts = await state.ReadAsync(session.CharacterId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Current skills are unavailable.");
            builds = await state.BuildsAsync(session.CharacterId, ct).ConfigureAwait(false);
            skills = new(entry!.Skills with { Pages = facts.Pages, SkillPointBasis = facts.PointBasis, ProtocolMirror = points });
        }
        ImmutableArray<ReplayPetState> pets = [];
        if (needs.HasFlag(ReplayProjectionNeeds.Pets) || appearanceNeeded)
            pets = (await Get<ICreatureDomainService>().GetStore(session.CharacterId).ReadAllAsync(ct).ConfigureAwait(false))
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
                Get<IEquippedCreatureQuery>().GetItemId(characterInventory!), entry.Public.DerivedStats.EquippedCreatureLevel)
            { SouthernDaleQuestAvailable = await Get<ISouthernDaleQuestDomainService>().CanAcceptAsync(session.CharacterId, ct).ConfigureAwait(false) };
            var offers = await Get<Dfo.Server.Abstractions.Quests.IQuestAvailabilityRuleService>().ReadAvailableAsync(subject, ct).ConfigureAwait(false);
            quests = new(entry.Quests.ActiveQuests.ToImmutableArray(), entry.Quests.VisibilityFlags.ToImmutableArray(), offers.Quests.ToImmutableArray());
        }
        CharacterFatigueBalance? fatigue = null;
        if (needs.HasFlag(ReplayProjectionNeeds.Fatigue) || appearanceNeeded)
            fatigue = await Get<ICharacterFatigueDomainService>().BalanceAtAsync(session.CharacterId, at, ct).ConfigureAwait(false);
        ImmutableArray<ItemUsageState> itemUsage = [];
        if (needs.HasFlag(ReplayProjectionNeeds.ItemUsage))
            itemUsage = (await Get<IItemUsageDomainService>().GetSnapshotAtAsync(session.CharacterId, at, ct).ConfigureAwait(false)).ToImmutableArray();
        ImmutableArray<AccountPremiumEntryState> premiums = [];
        ImmutableArray<CharacterPremiumUsageSlotState> premiumUsage = [];
        if (needs.HasFlag(ReplayProjectionNeeds.Premium))
        {
            premiums = (await Get<IPremiumServiceDomainService>().For(session.AccountId).ReadAtAsync(at, ct).ConfigureAwait(false)).ToImmutableArray();
            premiumUsage = (await Get<IPremiumServiceUsageDomainService>().ReadAtAsync(session.CharacterId, at, ct).ConfigureAwait(false)).ToImmutableArray();
        }
        ReplayCharacterState? appearance = null;
        if (appearanceNeeded)
        {
            var character = await Get<ICharacterDomainService>().LoadForSelectionAsync(session.AccountId, identity!.SlotIndex, ct).ConfigureAwait(false);
            if (character.Identity.CharacterId != session.CharacterId)
                throw new InvalidDataException("Current character selection changed its owner.");
            entry = CharacterStateProjection.CompleteAppearanceProjection(character with { Identity = identity! }, entry!, characterInventory!);
            appearance = new(character with { Identity = identity, Progression = progress!,
                Fatigue = new(fatigue!.Value.Total, fatigue.Value.Used) { ConsumedToday = fatigue.Value.ConsumedToday } },
                entry!.Public with { Extras = entry.Public.Extras with { CharacterExperience = progress!.Experience,
                    SkillTreeIndex = builds!.IsUnlocked(new(1)) ? builds.Active.Value : -1 } });
        }
        safety.DemandCurrent(recipient);
        return new(session, request, at, inventory, pets, skills, quests, progress, points, fatigue,
            itemUsage, premiums, premiumUsage, appearance)
        { Rental = needs.HasFlag(ReplayProjectionNeeds.Rental) ? await Get<Dfo.Server.Abstractions.Rental.IEquipmentRentalDomainService>().PanelAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false) : null,
          CollectionBoxes = needs.HasFlag(ReplayProjectionNeeds.CollectionBox) ? await Get<ICollectionBoxDomainService>().ReadAsync(session.CharacterId, ct).ConfigureAwait(false) : null,
          Capsules = needs.HasFlag(ReplayProjectionNeeds.CoupleCapsules) ? await Get<Dfo.Server.Abstractions.Marriage.IMarriageCapsuleQueryDomainService>().ReadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false) : null,
          Housing = (needs.HasFlag(ReplayProjectionNeeds.MarriageHousing) || appearanceNeeded)
            ? await Get<Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService>().ReadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false) : null,
          MarriageInfo = (needs.HasFlag(ReplayProjectionNeeds.MarriageHousing) || appearanceNeeded)
            ? await Get<Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService>().InfoAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false) : null,
          LovePoints = needs.HasFlag(ReplayProjectionNeeds.MarriageLovePoints)
            ? (await Get<Dfo.Server.Abstractions.Marriage.IMarriageQueryDomainService>().SnapshotAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false)).LovePoints : null,
          HerasQuota = needs.HasFlag(ReplayProjectionNeeds.HerasQuota)
            ? await Get<IHerasDungeonDomainService>().ReadQuotaAtAsync(session.CharacterId, at, ct).ConfigureAwait(false) : null,
          EpicPieces = needs.HasFlag(ReplayProjectionNeeds.EpicBook)
            ? (await Get<IEpicBookDomainService>().GetBalancesAsync(session.AccountId, ct).ConfigureAwait(false)).ToImmutableArray() : default,
          Joust = needs.HasFlag(ReplayProjectionNeeds.Joust)
            ? await Get<Dfo.Server.Abstractions.Events.IJoustProjectionDomainService>().ReadAsync(session.CharacterId,at,ct).ConfigureAwait(false) : null };
    }
    private T Get<T>() where T : notnull => services.GetRequiredService<T>();
}
