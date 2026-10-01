using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class CharacterExperienceDomainService(
    ICharacterTable characters, IExperienceRuleService experienceRules,
    IExperienceCalculationDomainService calculation, IApplyEffectsDomainService effects)
    : ICharacterExperienceDomainService
{
    public uint? ThresholdForLevel(byte level)
    {
        var thresholds = experienceRules.Progression().LevelThresholds;
        return level < 2 || thresholds.Count < level - 1 ? null : thresholds[level - 2];
    }

    public async ValueTask<bool> TrySetProgressAsync(
        CharacterId characterId, byte level, uint experience, CancellationToken ct) =>
        await characters.SetProgressAsync(characterId, level, experience, ct).ConfigureAwait(false) == 1;

    public async ValueTask<ExperienceAward?> ApplyAwardAsync(ExperienceCalculationContext source,
        DateTimeOffset at, SelectedSessionIdentity? session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var progress = await characters.FindProgressAsync(source.Occasion.Character, ct).ConfigureAwait(false);
        if (progress is null) return null;
        var before = new CharacterProgressionState(checked((byte)Math.Clamp(progress.Level, 1, byte.MaxValue)),
            checked((uint)Math.Max(0, progress.Experience)));
        var breakdown = await calculation.CalculateAsync(source, before.Level, ct).ConfigureAwait(false);
        var decision = experienceRules.DecideAward(before, breakdown);
        var result = await effects.ApplyAsync(new(source.Occasion.Account, source.Occasion.Character, at, session),
            [new CharacterExperienceEffect(decision)], ct).ConfigureAwait(false);
        return result.Changes<CharacterExperienceChange>().Single().Award;
    }
}
