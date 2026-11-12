using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.OneToOneChat;

/// <summary>What a session cleanup has to project for the 1:1 conversations it ended.</summary>
/// <remarks>
/// A cleanup plan is delivered from more than one place -- disconnect, return to selection and a
/// selection that replaces an older session -- and each of them must tell the members left behind.
/// Each place projects these after its party notices, so the rule of who hears what lives here once.
/// </remarks>
internal static class ConversationDepartureNotices
{
    public static IEnumerable<(NotificationAudience Audience, ChatConversationLeft Notification)> For(
        SelectedSessionCleanupNotificationPlan plan) =>
        plan.ConversationDepartures.Select(departure => (
            (NotificationAudience)new NotificationAudience.TheseSessions([departure.Remaining.Session]),
            new ChatConversationLeft(departure)));
}
