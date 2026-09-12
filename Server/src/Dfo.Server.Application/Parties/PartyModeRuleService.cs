using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Parties;

internal sealed class PartyModeRuleService : IPartyModeRuleService
{
    public byte MaximumCapacity => 4;

    private static readonly PartySettings NormalDefaults = new(
        [0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, byte.MaxValue, byte.MaxValue]);

    public PartySettings DefaultSettings => NormalDefaults;

    public PartySettings Normalize(PartySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.IsReturnUserParty
            ? settings.WithReturnUserParty(enabled: false)
            : settings;
    }

    public PartyModeRuleFailure Validate(PartySettings settings, int memberCount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Category))
            return PartyModeRuleFailure.UnknownCategory;
        if (settings.Capacity is < 1 || settings.Capacity > MaximumCapacity)
            return PartyModeRuleFailure.InvalidCapacity;
        return settings.Capacity < memberCount
            ? PartyModeRuleFailure.CapacityBelowMemberCount
            : PartyModeRuleFailure.None;
    }

    public PartyModeRuleFailure ValidateAdmission(
        PartySettings settings,
        int currentMemberCount) =>
        Validate(settings, checked(currentMemberCount + 1));
}
