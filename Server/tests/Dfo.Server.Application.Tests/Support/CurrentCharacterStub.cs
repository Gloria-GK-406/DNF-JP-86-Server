using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Tests.Support;

internal sealed class CurrentCharacterStub : ICharacter
{
    public CharacterId Id { get; init; } = new(60);

    public ValueTask<Dfo.Server.Abstractions.World.ExperienceAward?> AddExpAsync(Dfo.Server.Abstractions.World.ExperienceCalculationContext source, DateTimeOffset at, Dfo.Server.Data.World.SelectedSessionIdentity? session, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<Dfo.Server.Abstractions.World.ExperienceAward> AddExpAsync(Dfo.Server.Abstractions.World.ExperienceBreakdown reward, CharacterProgressionState? expected, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<CharacterProgressionState> SetExpAsync(long experience, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<CharacterProgressionState> SetLevelAsync(byte level, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<bool> TrySetExpAsync(CharacterProgressionState expected, long experience, CancellationToken ct) => throw new NotSupportedException();
    public CharacterProgressionState? Progress { get; set; } = new(60, 1000);
    public CharacterIdentityState? Identity { get; set; } = new(new(60), 0, [65], 0, 0);
    public CharacterBaseStats? Stats { get; set; } = CharacterBaseStats.Zero;
    public int Gold { get; set; }
    public Func<ValueTask<CharacterProgressionState?>>? ProgressReader { get; set; }
    public static ICharacterDomainService Domain(CurrentCharacterStub? character = null) =>
        new CharacterDomainService(null!, null!, _ => character ?? new CurrentCharacterStub(), null!);
    public ValueTask<CharacterBaseStats?> BaseStatsAsync(CancellationToken token) => ValueTask.FromResult(Stats);
    public ValueTask<CharacterProgressionState?> ProgressAsync(CancellationToken token) => ProgressReader?.Invoke() ?? ValueTask.FromResult(Progress);
    public ValueTask<CharacterIdentityState?> IdentityAsync(CancellationToken token) => ValueTask.FromResult(Identity);
    public ValueTask<IReadOnlyList<CharacterLearnedSkillPage>?> LearnedSkillsAsync(CancellationToken token) => throw new NotSupportedException();
    public ValueTask<CharacterSkillPointLedger?> SkillPointLedgerAsync(CancellationToken token) => throw new NotSupportedException();
    public ValueTask<CharacterSkillProtocolMirrorState?> RemainingSkillPointsAsync(CancellationToken token) => throw new NotSupportedException();
    public ValueTask<ItemUseCandidate?> ItemUseFactsAsync(ItemCooldownKey key, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<ItemUsability> CanUseAsync(int itemId, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<int> GoldAsync(CancellationToken token) => ValueTask.FromResult(this.Gold);
    public ValueTask<int> AddGoldAsync(int amount, CancellationToken token)
    {
        this.Gold = checked(this.Gold + amount);
        return ValueTask.FromResult(this.Gold);
    }

    public ValueTask<int?> TrySpendGoldAsync(int amount, CancellationToken token)
    {
        if (amount < 0 || amount > this.Gold)
            return ValueTask.FromResult<int?>(null);
        this.Gold -= amount;
        return ValueTask.FromResult<int?>(this.Gold);
    }
}
