using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterProgressionDomainService(ICharacterTable characters, IExperienceRuleService rules,
    ICharacterStatRuleService stats, ICharacterSkillStateDomainService skills,
    IDomainTransactionAccessor transactions) : ICharacterProgressionDomainService
{
    public async ValueTask<CharacterProgressionState> SetLevelAsync(CharacterId character, byte level, CancellationToken ct)
    {
        RequireTransaction();
        var thresholds = rules.Progression().LevelThresholds;
        if (level < 1 || level > Math.Min(86, thresholds.Count + 1)) throw new ArgumentOutOfRangeException(nameof(level));
        var standing = await characters.FindStandingAsync(character, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Character is unavailable.");
        var before = await characters.FindProgressAsync(character, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Character is unavailable.");
        await stats.InitializeAsync(ct).ConfigureAwait(false);
        _ = stats.GetStats(CharacterCareer.FromPacked(checked((byte)standing.Job), checked((byte)standing.GrowType)), level);
        var next = new CharacterProgressionState(level, level == 1 ? 0 : thresholds[level - 2]);
        if (!await TryApplyAsync(character, new(checked((byte)before.Level), checked((uint)before.Experience)), next, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Character progression changed during settlement.");
        await skills.ReconcileLevelAsync(character, ct).ConfigureAwait(false);
        return next;
    }
    public async ValueTask<bool> TryApplyAsync(CharacterId character, CharacterProgressionState expected,
        CharacterProgressionState next, CancellationToken ct)
    {
        RequireTransaction();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(character.Value);
        if (expected.Level == 0 || next.Level is < 1 or > 86) throw new ArgumentOutOfRangeException(nameof(next));
        var current = await characters.FindProgressAsync(character, ct).ConfigureAwait(false);
        if (current is null || current.Level != expected.Level || current.Experience != expected.Experience) return false;
        return expected == next || await characters.SetProgressAsync(character, next.Level, next.Experience, ct).ConfigureAwait(false) == 1;
    }
    private void RequireTransaction()
    {
        if (transactions.Current is null) throw new InvalidOperationException("Progression mutations require a domain transaction.");
    }
}
