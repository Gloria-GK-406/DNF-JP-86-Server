using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Networking;

namespace Dfo.Server.Abstractions.Effects;

[Flags]
public enum ReplayProjectionNeeds
{
    None = 0, Inventory = 1, Pets = 2, Skills = 4, Quests = 8, Experience = 16,
    Fatigue = 32, ItemUsage = 64, Premium = 128, CharacterAppearance = 256,
    TownLocation = 512, Joust = 1024, EpicBook = 2048, AvatarCompound = 4096, HerasQuota = 8192, MarriageLovePoints = 16384, MarriageHousing = 32768, CoupleCapsules = 65536, CollectionBox = 131072, Rental = 262144,
    // UI recovery is opt-in for an actual compound request, never part of a generic full snapshot.
    All = Inventory | Pets | Skills | Quests | Experience | Fatigue | ItemUsage | Premium | CharacterAppearance | TownLocation | Joust | EpicBook | CollectionBox,
}

/// <summary>Only projection categories and physical addresses, never old receipts or reward increments.</summary>
public sealed record ReplayProjectionRequest(ReplayProjectionNeeds Needs, ImmutableArray<BusinessSlot> TouchedSlots)
{
    public ImmutableArray<TownAreaKey> TouchedTownAreas { get; init; } = [];
    public void Validate()
    {
        if ((Needs & ~(ReplayProjectionNeeds.All | ReplayProjectionNeeds.AvatarCompound | ReplayProjectionNeeds.HerasQuota | ReplayProjectionNeeds.MarriageLovePoints | ReplayProjectionNeeds.MarriageHousing | ReplayProjectionNeeds.CoupleCapsules | ReplayProjectionNeeds.Rental)) != 0 || TouchedSlots.IsDefault)
            throw new InvalidDataException("Unknown replay projection flags or unavailable slot collection.");
        if (Needs.HasFlag(ReplayProjectionNeeds.AvatarCompound) && !Needs.HasFlag(ReplayProjectionNeeds.Inventory))
            throw new InvalidDataException("Compound UI recovery requires a current inventory snapshot.");
        if (TouchedTownAreas.IsDefault || TouchedTownAreas.Any(x => x.ChannelId <= 0)
            || TouchedTownAreas.Length > 0 && !Needs.HasFlag(ReplayProjectionNeeds.TownLocation))
            throw new InvalidDataException("Touched town areas require a valid location projection.");
        if (TouchedSlots.Length > 0 && !Needs.HasFlag(ReplayProjectionNeeds.Inventory))
            throw new InvalidDataException("Touched physical slots require an inventory projection.");
        var mapping = new InventorySlotMappingRuleService();
        foreach (var slot in TouchedSlots)
        {
            var game = mapping.ToGameSlot(slot);
            if (game.Space is not (InventoryContainerType.Main or InventoryContainerType.Avatar
                or InventoryContainerType.PersonalCargo or InventoryContainerType.Equipment
                or InventoryContainerType.Pet or InventoryContainerType.AccountCargo or InventoryContainerType.QuickSlot))
                throw new InvalidDataException("This physical container has no replay projection.");
        }
    }
}

public sealed record ReplayPetState(long PetKey, int Stomach, byte ModeFlag, int Experience,
    int Mode1Field0A, int Mode1Field0B, int Level, ImmutableArray<byte> Name, byte TailFlag)
{
    public static ReplayPetState From(CharacterPetRuntimeState pet) => new(pet.PetKey, pet.Field04,
        pet.ModeFlag, pet.ProgressValue, pet.Mode1Field0A, pet.Mode1Field0B, pet.FieldAfterValue,
        pet.CreatureText.ToImmutableArray(), pet.TailFlag);
    public CharacterPetRuntimeState ToState() => new(PetKey, Stomach, ModeFlag, Experience,
        Mode1Field0A, Mode1Field0B, Level, Name.ToArray(), TailFlag);
}

/// <summary>Privately owns mutable legacy buffers; every outbound adapter receives a fresh copy.</summary>
public sealed class ReplayInventoryState
{
    private readonly CharacterInventoryEntryState character;
    private readonly AccountInventoryEntryState account;
    public ReplayInventoryState(CharacterInventoryEntryState character, AccountInventoryEntryState account)
    { this.character = Copy(character); this.account = Copy(account); }
    public CharacterInventoryEntryState Character() => Copy(character);
    public AccountInventoryEntryState Account() => Copy(account);
    private static InventoryContainerState Copy(InventoryContainerState state) => state with
    {
        Items = state.Items.Select(item => item with
        {
            AvatarDetail = item.AvatarDetail is { } avatar
                ? avatar with { JewelSocket = avatar.JewelSocket.ToArray() } : null,
        }).ToImmutableArray(),
    };
    private static CharacterInventoryEntryState Copy(CharacterInventoryEntryState state) => state with
    {
        Containers = state.Containers.Select(Copy).ToImmutableArray(),
        VirtualResources = state.VirtualResources.ToImmutableArray(),
        ItemValues = new(state.ItemValues.Cooldowns.ToImmutableArray(), state.ItemValues.Effects.ToImmutableArray()),
        EquipmentLocks = state.EquipmentLocks.ToImmutableArray(),
        Rentals = new(state.Rentals.Items.ToImmutableArray()),
    };
    private static AccountInventoryEntryState Copy(AccountInventoryEntryState state) => state with
    {
        Cargo = state.Cargo with { Container = Copy(state.Cargo.Container), EquipmentLocks = state.Cargo.EquipmentLocks.ToImmutableArray() },
        VirtualResources = state.VirtualResources.ToImmutableArray(),
    };
}

public sealed class ReplaySkillState
{
    private readonly CharacterSkillsEntryState skills;
    public ReplaySkillState(CharacterSkillsEntryState skills) => this.skills = Copy(skills);
    public CharacterSkillsEntryState ToState() => Copy(skills);
    private static CharacterSkillsEntryState Copy(CharacterSkillsEntryState state) => state with
    {
        Pages = state.Pages.Select(page => page with
        {
            Skills = page.Skills.Select(skill => skill with { ExtraValues = skill.ExtraValues.ToImmutableArray() }).ToImmutableArray(),
        }).ToImmutableArray(),
        SkillPointSlots = state.SkillPointSlots.ToImmutableArray(),
        DarkKnightCombos = state.DarkKnightCombos?.Select(combo => combo with { EncodedBody = combo.EncodedBody.ToArray() }).ToImmutableArray(),
        KnightShieldDeck = state.KnightShieldDeck?.ToImmutableArray(),
    };
}

public sealed record ReplayQuestState(ImmutableArray<CharacterQuestProgressState> Carried,
    ImmutableArray<byte> Visibility, ImmutableArray<OfferableQuestState> Offers);

public sealed class ReplayCharacterState
{
    private readonly ActiveCharacterState character;
    private readonly CharacterPublicEntryState publicState;
    public ReplayCharacterState(ActiveCharacterState character, CharacterPublicEntryState publicState)
    { this.character = Copy(character); this.publicState = Copy(publicState); }
    public ActiveCharacterState Character() => Copy(character);
    public CharacterPublicEntryState Public() => Copy(publicState);
    private static ActiveCharacterState Copy(ActiveCharacterState state) => state with
    { Identity = state.Identity with { Name = state.Identity.Name.ToArray() } };
    private static CharacterPublicEntryState Copy(CharacterPublicEntryState state) => state with
    {
        Condition = state.Condition with { CreatureBuffer = state.Condition.CreatureBuffer?.ToArray() },
        Pvp = state.Pvp with { Results = state.Pvp.Results.ToImmutableArray() },
        Extras = state.Extras with
        { Dimensions = state.Extras.Dimensions.ToImmutableArray(), SpecialRewardQuestIds = state.Extras.SpecialRewardQuestIds.ToImmutableArray() },
        Appearance = state.Appearance with { EncodedEntries = state.Appearance.EncodedEntries.ToArray() },
        Display = state.Display is { } display ? display with { Parts = display.Parts.Select(
            part => part with { ExpansionData = part.ExpansionData.ToArray() }).ToImmutableArray() } : null,
    };
}

public sealed record ReplayStateSnapshot(SelectedSessionIdentity Session, ReplayProjectionRequest Request,
    DateTimeOffset At, ReplayInventoryState? Inventory, ImmutableArray<ReplayPetState> Pets,
    ReplaySkillState? Skills, ReplayQuestState? Quests, CharacterProgressionState? Progress,
    CharacterSkillProtocolMirrorState? Points, CharacterFatigueBalance? Fatigue,
    ImmutableArray<ItemUsageState> ItemUsage, ImmutableArray<AccountPremiumEntryState> Premiums,
    ImmutableArray<CharacterPremiumUsageSlotState> PremiumUsage, ReplayCharacterState? Appearance)
{
    public Dfo.Server.Abstractions.Rental.EquipmentRentalPanel? Rental { get; init; }
    public CollectionBoxSnapshot? CollectionBoxes { get; init; }
    public Dfo.Server.Data.Marriage.MarriageCapsuleSnapshot? Capsules { get; init; }
    public Dfo.Server.Data.Marriage.HousingSnapshot? Housing { get; init; }
    public Dfo.Server.Data.Marriage.MarriageInfoSnapshot? MarriageInfo { get; init; }
    public int? LovePoints { get; init; }
    public HerasQuotaSnapshot? HerasQuota { get; init; }
    public Dfo.Server.Data.Events.JoustState? Joust { get; init; }
    public ImmutableArray<EpicPieceBalance> EpicPieces { get; init; }
}

public interface IReplayStateDomainService
{
    ValueTask<ReplayStateSnapshot> ReadAsync(SelectedSessionIdentity session,
        ReplayProjectionRequest request, GameMessageMetadata? metadata, CancellationToken cancellationToken);
}

public interface IReplayStateProjectionDelivery
{
    ValueTask RefreshAsync(NetworkConnectionContext originalConnection, SelectedSessionIdentity session,
        ReplayProjectionRequest request, GameMessageMetadata? metadata, CancellationToken cancellationToken);
}

public interface IReplayStateProjectionSink
{
    ValueTask ProjectAsync(NetworkConnectionContext originalConnection, SelectedSessionIdentity session,
        GameMessageMetadata? metadata, ReplayStateSnapshot snapshot, ICommandRegistrationAttempt? attempt, CancellationToken cancellationToken);
}
