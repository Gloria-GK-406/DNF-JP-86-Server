using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Events;
internal sealed class EventPlayerProjectionDomainService(IEventManagementDomainService events,IWeeklyAttendanceDomainService attendance,
    IJoustProjectionDomainService joust, TimeProvider clock, ILuckyCharacterEventQuery luckyCharacters,
    ILuckyCharacterBurning burning, ICharacterDomainService characters, IChannelEntryDomainService channels)
    : IEventPlayerProjectionDomainService
{
    public async ValueTask<EventPlayerSnapshot> ReadNowAsync(SelectedSessionIdentity session,CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var lucky = await luckyCharacters.ReadAtAsync(now, ct).ConfigureAwait(false);
        if (lucky.Revision == 0)
            return await BuildAsync(session.AccountId, session.CharacterId, -1, false, now, lucky, ct).ConfigureAwait(false);
        var identity = await characters.Get(session.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var channel = await channels.ResolveAsync(session.ListenerPort, ct).ConfigureAwait(false);
        return await BuildAsync(session.AccountId, session.CharacterId, identity is null ? -1 : identity.Job,
            channel.IsPvp, now, lucky, ct)
            .ConfigureAwait(false);
    }

    public async ValueTask<EventPlayerSnapshot> ReadAtAsync(AccountId account, CharacterId character, int job,
        bool pvp, DateTimeOffset at, CancellationToken ct) =>
        await BuildAsync(account, character, job, pvp, at,
            await luckyCharacters.ReadAtAsync(at, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    private async ValueTask<EventPlayerSnapshot> BuildAsync(AccountId account, CharacterId character, int job,
        bool pvp, DateTimeOffset at, LuckyCharacterPeriod lucky, CancellationToken ct)
    {
        var information = await events.GetEventInfoListAsync(account, at, ct).ConfigureAwait(false);
        byte? fatigueState = null;
        if (pvp)
            lucky = lucky with { Jobs = [] };
        else if (lucky.Revision > 0)
        {
            fatigueState = lucky.Includes(job) && burning.IsEnabled(character, lucky.Revision) ? (byte)2 : (byte)0;
        }
        return EventPlayerSnapshotProjection.WithScheduledDungeons(new(information,
            await attendance.StateAsync(account, at, ct).ConfigureAwait(false),
            await joust.ReadAsync(character, at, ct).ConfigureAwait(false))
        {
            LuckyCharacters = lucky,
            LuckyFatigueState = fatigueState,
        });
    }
}
