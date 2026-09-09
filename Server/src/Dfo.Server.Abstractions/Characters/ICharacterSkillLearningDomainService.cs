using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterSkillLearningDomainService
{
    // The caller holds the online-user and account execution gates and an ambient transaction.
    ValueTask<CharacterSkillLearningResult> LearnAsync(SelectedSessionIdentity session, byte pageIndex,
        IReadOnlyList<CharacterSkillLearningChange> changes, byte mode, CancellationToken cancellationToken);
}
