using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterSkillLearningDomainService
{
    ValueTask<bool> SwapSlotsAsync(SelectedSessionIdentity session, int pageIndex, int first, int second, CancellationToken cancellationToken);

    ValueTask<int?> SwitchAsync(SelectedSessionIdentity session, int expectedCurrent, int target, CancellationToken cancellationToken);
    ValueTask<int?> ToggleAsync(SelectedSessionIdentity session, int? expectedCurrent, CancellationToken cancellationToken);
    // The caller holds the online-user and account execution gates and an ambient transaction.
    ValueTask<CharacterSkillLearningResult> LearnAsync(SelectedSessionIdentity session, int pageIndex,
        IReadOnlyList<CharacterSkillLearningChange> changes, byte mode, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}

public interface ICharacterSkillGroupEditPolicy
{
    SkillGroup Group { get; }

    ValueTask<CharacterSkillLearningRejection> RefuseEditAsync(
        SelectedSessionIdentity session, CharacterSkillGroupEditOperation operation, CancellationToken cancellationToken);
}

public enum CharacterSkillGroupEditOperation
{
    Learn,
    SwapSlots,
    SwitchBuild,
}
