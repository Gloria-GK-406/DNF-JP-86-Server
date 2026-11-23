using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.ExpertJobs;

public interface IExpertJobStateStore
{
    ValueTask<ExpertJobState> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask WriteAsync(CharacterId character, ExpertJobState state, CancellationToken ct);
    ValueTask DeleteAsync(CharacterId character, CancellationToken ct);
}

public interface IExpertJobDomainService
{
    ICharacterExpertJob Get(CharacterId character);
}

public interface ICharacterExpertJob
{
    ValueTask<ExpertJobState> GetStateAsync(CancellationToken ct);
    ValueTask<IDisjointerDomainService?> GetDisjointerAsync(CancellationToken ct);
    ValueTask<bool> JoinFromQuestAsync(OwnedCharacterId owner, ushort questId, ExpertJobType type, CancellationToken ct);
    ValueTask<ExpertJobGiveUpResult> GiveUpAsync(SelectedSessionIdentity session, IEffectFootprintRecorder footprint, CancellationToken ct);
}

public enum ExpertJobGiveUpRejection { None, InvalidState, InsufficientGold }

public sealed record ExpertJobGiveUpResult(ExpertJobGiveUpRejection Rejection, int Gold = 0,
    byte GiveUpCount = 0, ApplyEffectsResult? Effects = null, ExpertStoreClosure? ClosedStore = null);

public sealed record ExpertJobDefinition(
    ExpertJobType Type,
    IReadOnlyList<uint> ExperienceThresholds,
    IReadOnlyList<int> GiveUpCosts,
    IReadOnlySet<int> ConnectedQuests,
    IReadOnlySet<int> ClearedOnGiveUp,
    IReadOnlyDictionary<int, byte> Skills,
    int RemovedItem,
    int InitialEndurance,
    IReadOnlyDictionary<int, int> AutoLearnRecipes,
    IReadOnlyDictionary<byte, int> CardQualificationLevelRequirements)
{
    public int MaximumStoreCharge { get; init; }
    public DisjointMachineDefinition? DisjointMachine { get; init; }

    public int LevelAt(uint experience) => Math.Min(ExperienceThresholds.Count,
        1 + ExperienceThresholds.TakeWhile(threshold => experience >= threshold).Count());

    public IReadOnlyList<int> RecipesAt(uint experience)
    {
        var level = LevelAt(experience);
        return AutoLearnRecipes.Where(pair => pair.Key <= level).Select(pair => pair.Value).Order().ToArray();
    }

    public IReadOnlyList<byte> CardQualificationsAt(uint experience)
    {
        var level = LevelAt(experience);
        return CardQualificationLevelRequirements.Where(pair => pair.Value <= level)
            .Select(pair => pair.Key).Order().ToArray();
    }
}

public interface IExpertJobDefinitionSource
{
    ValueTask<ExpertJobDefinition?> FindAsync(ExpertJobType type, CancellationToken ct);
}

public interface IExpertJobRuleService
{
    ValueTask<ExpertJobDefinition?> FindAsync(ExpertJobType type, CancellationToken ct);
    ValueTask<bool> ContainsGrantedSkillAsync(IReadOnlySet<int> ids, CancellationToken ct);
}
