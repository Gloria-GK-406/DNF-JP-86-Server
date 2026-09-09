using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Tests.Support;

internal sealed class CurrentCharacterStub : ICharacter
{
    public CharacterId Id { get; init; } = new(60);
    public CharacterProgressionState? Progress { get; set; } = new(60, 1000);
    public CharacterIdentityState? Identity { get; set; } = new(new(60), 0, [65], 0, 0);
    public CharacterBaseStats? Stats { get; set; } = CharacterBaseStats.Zero;
    public static ICharacterDomainService Domain(CurrentCharacterStub? character = null) =>
        new CharacterDomainService(null!, null!, _ => character ?? new CurrentCharacterStub());
    public ValueTask<CharacterBaseStats?> BaseStatsAsync(CancellationToken token) => ValueTask.FromResult(Stats);
    public ValueTask<CharacterProgressionState?> ProgressAsync(CancellationToken token) => ValueTask.FromResult(Progress);
    public ValueTask<CharacterIdentityState?> IdentityAsync(CancellationToken token) => ValueTask.FromResult(Identity);
    public ValueTask<IReadOnlyList<CharacterLearnedSkillPage>?> LearnedSkillsAsync(CancellationToken token) => throw new NotSupportedException();
    public ValueTask<CharacterSkillPointLedger?> SkillPointLedgerAsync(CancellationToken token) => throw new NotSupportedException();
    public ValueTask<CharacterSkillProtocolMirrorState?> RemainingSkillPointsAsync(CancellationToken token) => throw new NotSupportedException();
    public ValueTask<ItemUsability> CanUseAsync(int itemId, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<int> GoldAsync(CancellationToken token) => throw new NotSupportedException();
    public ValueTask<int> AddGoldAsync(int amount, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<int?> TrySpendGoldAsync(int amount, CancellationToken token) => throw new NotSupportedException();
}
