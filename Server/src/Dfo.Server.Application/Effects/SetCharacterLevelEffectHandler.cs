using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Effects;

internal sealed class SetCharacterLevelEffectHandler(ICharacterDomainService characters,
    IExperienceRuleService rules) : EffectHandler<SetCharacterLevelEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, SetCharacterLevelEffect effect, CancellationToken ct)
    {
        if (effect.TargetLevel < 1 || effect.TargetLevel > rules.CharacterScale().TopLevel)
            throw new ArgumentException("Invalid character level target.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SetCharacterLevelEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        var character = characters.Get(context.CharacterId);
        if (await character.ProgressAsync(ct).ConfigureAwait(false) != effect.Expected)
            throw new InvalidOperationException("Character progression changed during ticket use.");
        context.Footprint.DemandEffects(context.Owner);
        return new CharacterLevelChange(await character.SetLevelAsync(effect.TargetLevel, ct).ConfigureAwait(false));
    }
}
