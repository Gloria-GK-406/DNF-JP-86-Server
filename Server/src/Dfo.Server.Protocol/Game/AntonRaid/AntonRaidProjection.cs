using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed record AntonRaidTownArrived(SelectedSessionIdentity Session)
    : INotification<AntonRaidTownArrived>;

internal sealed class AntonRaidTownArrivedHandler(IAntonRaidDomainService raids, ISelectedCharacterCurrentViewDomainService views)
    : INotificationHandler<AntonRaidTownArrived>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, AntonRaidTownArrived notice, CancellationToken ct)
    {
        if (recipient.ExpectedSession != notice.Session || raids.RecruitmentChannel(notice.Session) is not { } channel)
            return [];

        var directory = raids.Directory(channel);
        var messages = await AntonRaidRecruitmentProjection.InitialAsync(views, directory, notice.Session, ct).ConfigureAwait(false);
        return [new AntonRaidTownLoaded(), .. messages];
    }
}

internal sealed record AntonRaidSessionArrived(SelectedSessionIdentity Session)
    : INotification<AntonRaidSessionArrived>;

internal sealed class AntonRaidSessionArrivedHandler(IAntonRaidDomainService raids)
    : INotificationHandler<AntonRaidSessionArrived>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, AntonRaidSessionArrived notice, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            recipient.ExpectedSession == notice.Session && raids.RejoinOffer(notice.Session) is { } offer
                ? [AntonRaidRuntimeNotice.Write(ServerMessageType.GameAntonRaidRejoinOffer, writer =>
                {
                    writer.Write(offer.Number);
                    writer.Write(offer.Channel);
                })] : []);
}

internal sealed record AntonRaidRecruitmentChanged(AntonRaidRecruitmentChange Change)
    : INotification<AntonRaidRecruitmentChanged>;

internal sealed class AntonRaidRecruitmentChangedHandler(ISelectedCharacterCurrentViewDomainService views)
    : INotificationHandler<AntonRaidRecruitmentChanged>
{
    // A notification handler scope covers one publish and all of its recipients.
    private IReadOnlyList<CurrentUserStateMessage>? profiles;

    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, AntonRaidRecruitmentChanged notice, CancellationToken ct)
    {
        profiles ??= await AntonRaidRecruitmentProjection.LoadMembersAsync(views, notice.Change.Raid?.Members ?? [], ct)
            .ConfigureAwait(false);
        return
        [
            .. profiles.Where(profile => profile.Character.Identity.CharacterId != recipient.CharacterId),
            new AntonRaidChangedNotice(notice.Change.Number, notice.Change.Raid),
            new AntonRaidDirectoryNotice(notice.Change.Directory),
        ];
    }
}

internal static class AntonRaidRecruitmentProjection
{
    internal static async ValueTask<IReadOnlyList<IGameServerMessage>> InitialAsync(
        ISelectedCharacterCurrentViewDomainService views, IReadOnlyList<AntonRaidRecruitmentSnapshot> directory,
        SelectedSessionIdentity viewer, CancellationToken ct)
    {
        var profiles = await LoadMembersAsync(views, directory.SelectMany(raid => raid.Members).Where(member => member.Session != viewer), ct)
            .ConfigureAwait(false);
        return [.. profiles, .. directory.Select(raid => new AntonRaidChangedNotice(raid.Number, raid)), new AntonRaidDirectoryNotice(directory)];
    }

    internal static async ValueTask<IReadOnlyList<CurrentUserStateMessage>> LoadMembersAsync(
        ISelectedCharacterCurrentViewDomainService views, IEnumerable<AntonRaidMember> members, CancellationToken ct)
    {
        var profiles = new List<CurrentUserStateMessage>();
        foreach (var member in members)
        {
            if (member.RejoinUntil is null && await views.TryLoadAsync(member.Session, ct).ConfigureAwait(false) is { } view)
                profiles.Add(ProfileOf(view));
        }
        return profiles;
    }

    internal static CurrentUserStateMessage ProfileOf(SelectCharacterResult view)
    {
        var participant = TownReturnProjection.Participant(view, view.Character.Position);
        return new(participant.Character, participant.Public, participant.Equipment, participant.Pets);
    }

    internal static async ValueTask DeliverAsync(ICommandProjections projections,
        AntonRaidRecruitmentChange change, CancellationToken ct)
    {
        if (change.Audience.Count > 0)
            await projections.ProjectAsync(new NotificationAudience.TheseSessions(change.Audience),
                new AntonRaidRecruitmentChanged(change), ct).ConfigureAwait(false);
    }
}

internal sealed record AntonRaidApprovalIssued(CurrentUserStateMessage Sender, AntonRaidApprovalNotice Invite)
    : INotification<AntonRaidApprovalIssued>;

internal sealed class AntonRaidApprovalIssuedHandler : INotificationHandler<AntonRaidApprovalIssued>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, AntonRaidApprovalIssued notice, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([notice.Sender, notice.Invite]);
}
