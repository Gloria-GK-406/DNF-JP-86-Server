using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// What a job starts with, and what a creation request may ask for.
/// </summary>
/// <remarks>
/// Bounds and job ceiling carried over from the legacy server, which is the only evidence we have
/// for them. The two byte bounds genuinely differ per protocol -- see <see cref="CharacterNameRules"/>.
/// </remarks>
internal sealed class CharacterCreationRuleService(
    ICharacterStatRuleService characterStats,
    IInitialSkillDefinitionSource initialSkills,
    IInitialEquipmentDefinitionSource initialEquipment) : ICharacterCreationRuleService
{
    private const int NameCheckMinBytes = 2;
    private const int NameCheckMaxBytes = 30;
    private const int CreateMinBytes = 2;
    private const int CreateMaxBytes = 18;
    private const byte MaxJobId = 12;

    /// <summary>The first slot of the skill band the client's skill bar does not draw from.</summary>
    private const short FirstPassiveSlot = 102;

    /// <summary>
    /// What the archive's equipment index says every worn item is.
    /// </summary>
    /// <remarks>
    /// Held here rather than asked of the item-placement rules. Reaching for those would make
    /// creating a character depend on the item-definition archive source being wired, which it has
    /// never needed and which several compositions do not register -- a large coupling to read one
    /// number. Two rule services carrying the same archive fact is an open question, left open
    /// rather than answered by making one of them heavier.
    /// </remarks>
    private const byte EquipmentKind = 1;

    public ushort AccountSlotLimit => 32;

    public bool IsKnownJob(byte job) => job <= MaxJobId;

    public CharacterNameShape InspectNameForCheck(byte[]? rawName) =>
        CharacterNameRules.Inspect(rawName, NameCheckMinBytes, NameCheckMaxBytes);

    public CharacterNameShape InspectNameForCreation(byte[]? rawName) =>
        CharacterNameRules.Inspect(rawName, CreateMinBytes, CreateMaxBytes);

    /// <summary>
    /// What a new character of this job is born with, in its slots.
    /// </summary>
    /// <remarks>
    /// Two skill bands, because the skill bar reads only the first. Everything written to the low
    /// slots reaches the bar in slot order, so granting the passives alongside the actives put
    /// 基础精通 and 基础防具精通 on F and G, where a player cannot use them and would not expect
    /// them; ordering the actives first was not enough, because the passives still landed in
    /// slots after them. Dropping the passives instead answered that, and cost the character
    /// the passives themselves.
    /// <para>
    /// The band the bar does not read is 102 upwards. That is not a choice made here: a working
    /// A21 session places a job's three initial passives at 102, 103 and 104 while its three
    /// actives sit at 0, 1 and 2, and this repository's own PvP profile already builds its
    /// slots the same way. Within each band the archive's order is kept; it is the only
    /// ordering available.
    /// </para>
    /// </remarks>
    public async ValueTask<NewCharacterStartingState> StartingStateAsync(
        byte job, CancellationToken cancellationToken)
    {
        await characterStats.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var stats = characterStats.GetStats(new CharacterCareer(job), 1);

        var grants = await initialSkills.GetAsync(job, cancellationToken).ConfigureAwait(false);
        var skills = new List<NewCharacterSkill>();
        short activeSlot = 0;
        short passiveSlot = FirstPassiveSlot;
        foreach (var grant in grants)
        {
            var slot = grant.IsActive ? activeSlot++ : passiveSlot++;
            skills.Add(new NewCharacterSkill(slot, grant.SkillId, grant.Level));
        }

        var equipment =
            (await initialEquipment.GetAsync(job, cancellationToken).ConfigureAwait(false))
            .Select(grant => new NewCharacterEquipment(
                grant.SlotIndex, grant.ItemId, grant.Durability, EquipmentKind))
            .ToArray();

        return new NewCharacterStartingState(stats, skills, equipment);
    }
}
