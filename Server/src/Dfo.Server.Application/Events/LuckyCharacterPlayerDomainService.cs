using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Events;

internal sealed class LuckyCharacterPlayerDomainService(
    ILuckyCharacterEventQuery events, ILuckyCharacterBurning burning, IChannelEntryDomainService channels,
    IOnlineCharacterRuntimeDomainService runtime, ICharacterDomainService characters, ICharacterFatigueDomainService fatigue,
    IDungeonEntryIntentDomainService entryIntents, IDungeonParticipationDomainService participation,
    LuckyCharacterBenefitRuleService rules, TimeProvider clock) : ILuckyCharacterPlayerDomainService
{
    public async ValueTask<LuckyCharacterPeriod> ReadRosterAsync(int listenerPort, CancellationToken ct)
    {
        var period = await events.ReadAtAsync(clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (period.Revision <= 0) return period;
        var channel = await channels.ResolveAsync(listenerPort, ct).ConfigureAwait(false);
        return channel.IsPvp ? period with { Jobs = [] } : period;
    }

    public async ValueTask<LuckyCharacterBurningResult> SetBurningAsync(
        SelectedSessionIdentity session, bool enabled, CancellationToken ct)
    {
        if (participation.Find(session.CharacterId) is not null)
            return new(LuckyCharacterBurningError.OutsideTown);
        var inOrdinaryDungeonSelection = entryIntents.Find(session)?.Kind is
            DungeonEntryIntentKind.Interactive or DungeonEntryIntentKind.Directed or DungeonEntryIntentKind.OrdinaryRechallenge;
        if (runtime.StandingOf(session) is null && !inOrdinaryDungeonSelection)
            return new(LuckyCharacterBurningError.OutsideTown);
        if ((await channels.ResolveAsync(session.ListenerPort, ct).ConfigureAwait(false)).IsPvp)
            return new(LuckyCharacterBurningError.Ineligible);
        var now = clock.GetUtcNow();
        var period = await events.ReadAtAsync(now, ct).ConfigureAwait(false);
        var identity = await characters.Get(session.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        if (identity is null || !period.Includes(identity.Job))
            return new(LuckyCharacterBurningError.Ineligible, false);
        if (enabled && !rules.CanEnableBurning((await fatigue.BalanceAtAsync(session.CharacterId, now, ct).ConfigureAwait(false)).Remaining))
            return new(LuckyCharacterBurningError.InsufficientFatigue, burning.IsEnabled(session.CharacterId, period.Revision));
        return burning.TrySet(session, period.Revision, enabled)
            ? new(LuckyCharacterBurningError.None, enabled)
            : new(LuckyCharacterBurningError.OutsideTown);
    }
}
