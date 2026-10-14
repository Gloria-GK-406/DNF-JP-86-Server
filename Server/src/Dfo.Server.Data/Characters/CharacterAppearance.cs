using Dfo.Server.Data.Game;

namespace Dfo.Server.Data.Characters;

// Native avatars are selected by career and visibility, not inventory template IDs.
public sealed record CharacterAvatarCandidates(CharacterCareer Identity)
{
    public CharacterCareer Default => new(Identity.Job);
    public CharacterCareer Career => new(Identity.Job, Identity.GrowType);
    public CharacterCareer Awakening => Identity.GrowType == 0 ? Default : Identity;
}

public sealed record CharacterAppearance(
    CharacterAvatarCandidates Avatars,
    CharacterCareer NativeAvatar,
    bool ShowGrowEffect,
    bool ShowFullAvatar,
    bool ShowAwakeningAvatar,
    byte VisibilityBits,
    uint TitleAppearanceItemId,
    IReadOnlyList<CharacterRosterAppearanceState> Parts);
