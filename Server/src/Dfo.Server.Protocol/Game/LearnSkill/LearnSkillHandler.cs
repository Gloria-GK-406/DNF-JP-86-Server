using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.LearnSkill;

public sealed class LearnSkillHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, ICommandBus commands, IGameMessageSender sender,
    ILogger<LearnSkillHandler> logger, IEventService notifications) : ClientMessageHandler<LearnSkillRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, LearnSkillRequest message,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        CharacterSkillLearningResult result;
        if (message.PageIndex > 1 || session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
            result = new(CharacterSkillLearningRejection.NoSession);
        else
        {
            try
            {
                result = await commands.SendAsync(new LearnSkillCommand(online.OnlineUserId, session,
                    message.PageIndex, message.Changes, message.Mode), cancellationToken).ConfigureAwait(false);
            }
            catch (OpeningRejectedException failure) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
            {
                logger.LogInformation("Skill refund consumable rejected: {Reason}", failure.Reason);
                result = new(CharacterSkillLearningRejection.ResetItemRequired);
            }
        }
        if (result.Receipt is { } receipt)
            logger.LogInformation("Skill learning character={CharacterId} page={Page} skills={Skills} SP={Sp} TP={Tp}",
                receipt.Session.CharacterId.Value, receipt.PageIndex,
                string.Join(",", receipt.Skills.Select(skill => skill.SkillId + ":" + skill.Level)),
                receipt.Ledger.For(new(receipt.PageIndex)).Sp,
                receipt.Ledger.For(new(receipt.PageIndex)).Tp);
        else
            logger.LogInformation("Skill learning {ConnectionId} page={Page} count={Count} refused={Reason}",
                context.Connection.ConnectionId, message.PageIndex, message.Changes.Count, result.Rejection);
        if (session is not null && sessions.FindByConnection(context.Connection.ConnectionId) != session)
            return;
        if (result.Receipt?.Inventory is { } inventory)
        {
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session!.CharacterId]),
                new OpeningCompleted(inventory, [new LearnSkillResponse(result.Receipt)]), cancellationToken);
            return;
        }
        await sender.SendCmdAsync(context.Connection.ConnectionId, new LearnSkillResponse(result.Receipt),
            cancellationToken).ConfigureAwait(false);
    }
}
