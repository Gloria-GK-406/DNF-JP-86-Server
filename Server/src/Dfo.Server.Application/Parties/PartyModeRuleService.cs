using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Parties;

internal sealed class PartyModeRuleService : IPartyModeRuleService
{
    public byte MaximumCapacity => 4;

    private static readonly PartySettings NormalDefaults = new(
        [0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0, 0]);

    public PartySettings DefaultSettings => NormalDefaults;

    public PartySettings Normalize(PartySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.IsReturnUserParty
            ? settings.WithReturnUserParty(enabled: false)
            : settings;
        // A21's edit UI serializes its retained selector even after choosing Normal
        // (VA 009C61B1..009C6226, 009C6507..009C6515). Normal has no mode target.
        return normalized.Category == PartyCategory.Normal && normalized.ModeTargetRaw != 0
            ? normalized.WithModeTarget(0)
            : normalized;
    }

    public PartyModeRuleFailure Validate(PartySettings settings, int memberCount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Category))
            return PartyModeRuleFailure.UnknownCategory;
        if (settings.Capacity is < 1 || settings.Capacity > MaximumCapacity)
            return PartyModeRuleFailure.InvalidCapacity;
        if (settings.Capacity < memberCount)
            return PartyModeRuleFailure.CapacityBelowMemberCount;
        return settings.Category == PartyCategory.Normal && settings.ModeTargetRaw != 0
            ? PartyModeRuleFailure.InvalidModeTarget
            : PartyModeRuleFailure.None;
    }

    public PartyModeRuleFailure ValidateAdmission(
        PartySettings settings,
        int currentMemberCount) =>
        Validate(settings, checked(currentMemberCount + 1));
}
