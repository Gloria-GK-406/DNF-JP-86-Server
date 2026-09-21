using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

public interface IWeakness
{
    byte Value { get; }
    ValueTask<int> RecoveryGoldCost(CancellationToken cancellationToken = default);
    ValueTask SetWeakness();
    void Recover();
    void PauseRecovery();
    void ResumeRecovery();
}

public interface IWeaknessDomainService
{
    IWeakness For(CharacterId characterId);
    TimeSpan RecoveryInterval { get; }
    IReadOnlyList<CharacterId> AdvanceRecovery();
    ValueTask<IReadOnlyList<CharacterId>> RetryPendingWeakness(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<CharacterId>>([]);
}

public interface IWeaknessRuleService
{
    byte? InitialValue(byte level);
    byte RecoverStep(byte value);
    int RecoveryGoldCost(byte level, byte value);
    TimeSpan RecoveryInterval { get; }
}
