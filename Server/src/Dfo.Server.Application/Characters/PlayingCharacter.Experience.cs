using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed partial class PlayingCharacter
{
    public async ValueTask<ExperienceAward> AddExpAsync(ExperienceBreakdown reward,
        CharacterProgressionState? expected, CancellationToken ct)
    {
        RequireProgressionTransaction();
        var before = await ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Character is unavailable.");
        if (expected is not null && before != expected)
            throw new InvalidOperationException("Character progression changed during settlement.");
        var award = experienceRules.DecideAward(before, reward).Award;
        // Resolve ownership before accepting any changes. Missing capsule state aborts the use case.
        if (award.Overflow > 0)
        {
            var owner = await characters.FindOwnerAsync(Id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Character owner is unavailable.");
            await capsules.AddAsync(owner, award.Overflow, ct).ConfigureAwait(false);
        }
        if (!await TrySetExpAsync(before, award.Total, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Character progression changed during settlement.");
        return award;
    }

    public ValueTask<CharacterProgressionState> SetLevelAsync(byte level, CancellationToken ct) =>
        SetExpAsync(experienceRules.CharacterScale().ExperienceForLevel(level), ct);

    public async ValueTask<CharacterProgressionState> SetExpAsync(long experience, CancellationToken ct)
    {
        RequireProgressionTransaction();
        var before = await ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Character is unavailable.");
        var next = experienceRules.CharacterScale().ProgressFor(experience);
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
        if (current is null || experienceRules.CharacterScale().ProgressFor(current.Experience) != expected) return false;
        var next = experienceRules.CharacterScale().ProgressFor(experience);
        if (current.Level == next.Level && current.Experience == next.Experience) return true;
        if (await characters.SetProgressAsync(Id, next.Level, next.Experience, ct).ConfigureAwait(false) != 1) return false;
        if (current.Level != next.Level)
            await events.EnqueueAsync(new Dfo.Server.Abstractions.Achievements.CharacterLevelChanged(Id, current.Level, next.Level), ct);
        return true;
    }

    private void RequireProgressionTransaction()
    {
        if (transactions.Current is null)
            throw new InvalidOperationException("Progression mutations require a domain transaction.");
    }
}
