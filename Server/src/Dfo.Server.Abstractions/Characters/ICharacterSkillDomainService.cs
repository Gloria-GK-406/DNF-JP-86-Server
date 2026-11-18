using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterSkillDomainStoreService
{
    /// <summary>Binds a skill service to a zero-based build ID without loading or activating it.</summary>
    ICharacterSkillDomainService Get(CharacterId characterId, SkillGroup group, SkillBuildId build);
}

public interface ICharacterSkillDomainService
{
    ValueTask<IReadOnlyList<CharacterSkillState>?> GetLearnedSkillsAsync(CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CharacterSkillState>?> GetHotkeySkillsAsync(CancellationToken cancellationToken);
    ValueTask<CharacterSkillPagePoints?> GetPointsAsync(CancellationToken cancellationToken);

    // The caller holds the relevant execution gates and an ambient write transaction.
    ValueTask<bool> SwapSlotsAsync(int first, int second, CancellationToken cancellationToken);
    ValueTask<CharacterSkillLearningResult> LearnAsync(SelectedSessionIdentity session,
        IReadOnlyList<CharacterSkillLearningChange> changes, IEffectFootprintRecorder footprint,
        CancellationToken cancellationToken);
}
