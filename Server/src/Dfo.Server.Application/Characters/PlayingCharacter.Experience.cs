using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed partial class PlayingCharacter
{
    private IExperienceRuleService ExperienceRules => experienceRules
        ?? throw new InvalidOperationException("Character experience rules are unavailable.");

    public async ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source,
        DateTimeOffset at, SelectedSessionIdentity? session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireProgressionTransaction();
        if (source.Occasion.Character != Id || session is not null && session.CharacterId != Id)
            throw new ArgumentException("Experience source names a different character.");
        if (await ProgressAsync(ct).ConfigureAwait(false) is not { } before) return null;
        if (await characters.FindOwnerAsync(Id, ct).ConfigureAwait(false) != source.Occasion.Account)
            throw new InvalidOperationException("Experience source names a different owner.");
        var reward = await experienceCalculation!.CalculateAsync(source, before.Level, ct).ConfigureAwait(false);
        var decision = ExperienceRules.DecideAward(before, reward);
        var result = await experienceEffects!().ApplyAsync(new(source.Occasion.Account, Id, at, session),
            [new Dfo.Server.Abstractions.Effects.CharacterExperienceEffect(decision)], ct).ConfigureAwait(false);
        return result.Changes<Dfo.Server.Abstractions.Effects.CharacterExperienceChange>().Single().Award;
    }

    public async ValueTask<ExperienceAward> AddExpAsync(ExperienceBreakdown reward,
        CharacterProgressionState? expected, CancellationToken ct)
    {
        RequireProgressionTransaction();
        var before = await ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Character is unavailable.");
        if (expected is not null && before != expected)
            throw new InvalidOperationException("Character progression changed during settlement.");
        var award = ExperienceRules.DecideAward(before, reward).Award;
        // Resolve ownership before accepting any changes. Missing capsule state aborts the use case.
        if (award.Overflow > 0)
        {
            var owner = await characters.FindOwnerAsync(Id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Character owner is unavailable.");
            await capsules!.AddAsync(owner, award.Overflow, ct).ConfigureAwait(false);
        }
        if (!await TrySetExpAsync(before, award.Total, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Character progression changed during settlement.");
        return award;
    }

    public ValueTask<CharacterProgressionState> SetLevelAsync(byte level, CancellationToken ct) =>
        SetExpAsync(ExperienceRules.CharacterScale().ExperienceForLevel(level), ct);

    public async ValueTask<CharacterProgressionState> SetExpAsync(long experience, CancellationToken ct)
    {
        RequireProgressionTransaction();
        var before = await ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Character is unavailable.");
        var next = ExperienceRules.CharacterScale().ProgressFor(experience);
        if (next.Level != before.Level)
        {
            var identity = await IdentityAsync(ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Character is unavailable.");
            await statRules.InitializeAsync(ct).ConfigureAwait(false);
            _ = statRules.GetStats(CharacterCareer.FromPacked(identity.Job, identity.GrowType), next.Level);
        }
        if (!await TrySetExpAsync(before, next.Experience, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Character progression changed during settlement.");
        // Explicit setters may lower the level; reconcile free skills just as SetLevel did before.
        await skillState.ReconcileLevelAsync(Id, ct).ConfigureAwait(false);
        return next;
    }

    public async ValueTask<bool> TrySetExpAsync(CharacterProgressionState expected, long experience, CancellationToken ct)
    {
        RequireProgressionTransaction();
        var current = await characters.FindProgressAsync(Id, ct).ConfigureAwait(false);
        if (current is null || ExperienceRules.CharacterScale().ProgressFor(current.Experience) != expected) return false;
        var next = ExperienceRules.CharacterScale().ProgressFor(experience);
        return current.Level == next.Level && current.Experience == next.Experience
            || await characters.SetProgressAsync(Id, next.Level, next.Experience, ct).ConfigureAwait(false) == 1;
    }

    private void RequireProgressionTransaction()
    {
        if (transactions?.Current is null)
            throw new InvalidOperationException("Progression mutations require a domain transaction.");
    }
}
