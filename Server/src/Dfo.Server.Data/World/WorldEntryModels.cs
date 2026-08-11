namespace Dfo.Server.Data.World;

/// <summary>A quest this character may take right now.</summary>
/// <remarks>
/// This is an answer, not an ingredient. The catalog has already been filtered by the
/// character's level, job, grow type and cleared set, so a consumer projects it directly
/// instead of re-deciding who qualifies.
/// </remarks>
public sealed record OfferableQuestState(int QuestId, string Name, int GrowTypeRequirement);

/// <summary>Migration matrix row 12: the quest list offered to this character.</summary>
public sealed record AvailableQuestEntryState(IReadOnlyList<OfferableQuestState> Quests)
{
    public static AvailableQuestEntryState Empty { get; } = new(Array.Empty<OfferableQuestState>());
}

/// <summary>One challenge slot this character's level opens, and what may fill it.</summary>
public sealed record DailyChallengeSlotState(int SlotIndex, IReadOnlyList<int> QuestIds);

/// <summary>
/// One daily-activity group as it stands for this character: the slots their level opens,
/// whether they have taken the reward, and the reward their level earns.
/// </summary>
/// <param name="RewardItemTemplateId">
/// The item the reward band grants, or zero when the group declares no reward for this
/// character's level.
/// </param>
public sealed record DailyChallengeGroupEntryState(
    int GroupId,
    int OpenSlotCount,
    IReadOnlyList<DailyChallengeSlotState> OpenSlots,
    bool RewardClaimed,
    int RewardItemTemplateId,
    int RewardCount);

/// <summary>Migration matrix row 14: today's challenge state for this character.</summary>
public sealed record DailyChallengeEntryState(IReadOnlyList<DailyChallengeGroupEntryState> Groups)
{
    public static DailyChallengeEntryState Empty { get; } = new(Array.Empty<DailyChallengeGroupEntryState>());
}

/// <summary>One position in a collection box, and whether this character has filled it.</summary>
public sealed record CollectionBoxCellState(
    int Column,
    int Row,
    int ItemTemplateId,
    int Tier,
    bool Filled);

/// <summary>
/// Migration matrix rows 32 and 33: one configured collection box with this character's
/// progress already applied to its grid.
/// </summary>
public sealed record CollectionBoxEntryState(
    int BoxIndex,
    int Columns,
    int Rows,
    IReadOnlyList<CollectionBoxCellState> Cells)
{
    public int FilledCount
    {
        get
        {
            var filled = 0;
            foreach (var cell in Cells)
            {
                if (cell.Filled)
                    filled++;
            }

            return filled;
        }
    }
}

/// <summary>
/// Migration matrix row 58: the greeting the equipped creature offers on entry.
/// </summary>
/// <remarks>
/// A null <c>PetWelcome</c> on the composed state means no welcome applies — either no
/// creature is equipped or the equipped one supplies no lines. That is why the entry packet
/// is skipped rather than padded, so the distinction must survive to the consumer.
/// </remarks>
public sealed record PetWelcomeEntryState(
    int CreatureId,
    string Name,
    IReadOnlyList<string> Lines);

/// <summary>
/// The static content of a character's entry, resolved against that character's progress.
/// </summary>
public sealed record WorldContentEntryState(
    AvailableQuestEntryState AvailableQuests,
    DailyChallengeEntryState DailyChallenge,
    IReadOnlyList<CollectionBoxEntryState> CollectionBoxes,
    PetWelcomeEntryState? PetWelcome);

/// <summary>Where a character stands when they enter.</summary>
/// <param name="IsTransient">
/// True when the character is placed for this visit only. A transient placement is not
/// written back, so the character returns to where they were once they leave.
/// </param>
public sealed record EntrySpawnState(
    byte TownId,
    byte AreaId,
    short X,
    short Y,
    byte Direction,
    byte AreaState,
    bool IsTransient);

/// <summary>
/// What the channel decides about this entry.
/// </summary>
/// <param name="PersistsPosition">
/// Whether the resolved position is written back to the character.
/// </param>
/// <param name="PvpSkillProfileApplies">
/// Whether a duelling skill profile applies at all on this channel. It says nothing about
/// the profile's contents: a profile that applies and turns out to be empty is a different
/// fact, and lives with the character's skills.
/// </param>
public sealed record ChannelEntryPolicyState(
    int ChannelId,
    EntrySpawnState Spawn,
    bool PersistsPosition,
    bool PvpSkillProfileApplies);
