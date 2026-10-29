using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterExperienceAwardDomainService(ICharacterDomainService characters,
    IExperienceCalculationDomainService calculation, IExperienceRuleService rules, IApplyEffectsDomainService effects,
    IDomainTransactionAccessor transactions) : ICharacterExperienceAwardDomainService
{
    public async ValueTask<ExperienceAward?> AwardAsync(ExperienceCalculationContext source, DateTimeOffset at,
        SelectedSessionIdentity? session, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var id = source.Occasion.Character;
        footprint.DemandEffects(new(source.Occasion.Account, id, session));
        if (transactions.Current is null)
            throw new InvalidOperationException("Progression mutations require a domain transaction.");
        if (session is not null && session.CharacterId != id)
            throw new ArgumentException("Experience source names a different character.");
        if (session is not null && session.AccountId != source.Occasion.Account)
            throw new ArgumentException("Experience source names a different owner.");
        if (await characters.Get(id).ProgressAsync(ct).ConfigureAwait(false) is not { } before) return null;
        var reward = await calculation.CalculateAsync(source, before.Level, ct).ConfigureAwait(false);
        var decision = rules.DecideAward(before, reward);
        var result = await effects.ApplyAsync(new(source.Occasion.Account, id, at, footprint, session),
            [new CharacterExperienceEffect(decision)], ct).ConfigureAwait(false);
        return result.Changes<CharacterExperienceChange>().Single().Award;
    }
}
