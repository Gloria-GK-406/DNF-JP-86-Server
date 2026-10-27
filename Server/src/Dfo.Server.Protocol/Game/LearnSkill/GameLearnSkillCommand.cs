using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Characters;
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

public sealed record GameLearnSkillCommand(NetworkDispatchContext Dispatch, LearnSkillRequest Input) : IProtocolCommand<CharacterSkillLearningResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameLearnSkillCommandHandler(IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions, ICharacterSkillLearningDomainService learning, ICommandWorkUnits units,
    IGameCommandOutbox outbox, ILogger<GameLearnSkillCommandHandler> logger, ICommandProjections projections)
    : ICommandHandler<GameLearnSkillCommand, CharacterSkillLearningResult>
{
    public async ValueTask<CharacterSkillLearningResult> HandleAsync(CommandExecutionContext context, GameLearnSkillCommand command, CancellationToken cancellationToken)
    {
        var request = context.Request ?? throw new InvalidOperationException("Skill learning requires its explicit request.");
        var message = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        CharacterSkillLearningResult result;
        if (message.PageIndex > 1 || session is null || request is CommandRequestContext.ProtocolEffectRejected || !onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null)
            result = new(CharacterSkillLearningRejection.NoSession);
        else
        {
            if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
                throw new InvalidOperationException("Skill learning requires its originally selected actor.");
            result = await units.WriteAsync(context, new("LearnSkill", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) =>
                {
                    var footprint=unit.Footprint;
                    try { return await learning.LearnAsync(session,message.PageIndex,message.Changes,message.Mode,footprint,token).ConfigureAwait(false); }
                    catch (OpeningRejectedException failure) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
                    {
                        unit.RequireRollback(failure);
                        logger.LogInformation("Skill refund consumable rejected: {Reason}",failure.Reason);
                        return new CharacterSkillLearningResult(CharacterSkillLearningRejection.ResetItemRequired);
                    }
                },cancellationToken).ConfigureAwait(false);
        }
        if (result.Receipt is { } receipt)
            logger.LogInformation("Skill learning character={CharacterId} page={Page} skills={Skills} SP={Sp} TP={Tp}",
                receipt.Session.CharacterId.Value, receipt.PageIndex,
                string.Join(",", receipt.Skills.Select(skill => skill.SkillId + ":" + skill.Level)),
                receipt.Ledger.For(new(receipt.PageIndex)).Sp,
                receipt.Ledger.For(new(receipt.PageIndex)).Tp);
        else
            logger.LogInformation("Skill learning {ConnectionId} page={Page} count={Count} refused={Reason}",
                command.Dispatch.Connection.ConnectionId, message.PageIndex, message.Changes.Count, result.Rejection);
        if (session is not null && !sessions.IsInWorld(session))
            return result;
        if (result.Receipt?.Inventory is { } inventory)
        {
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session!.CharacterId]),
                new OpeningCompleted(inventory, [new LearnSkillResponse(result.Receipt)]), cancellationToken);
            return result;
        }
        await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, new LearnSkillResponse(result.Receipt),
            cancellationToken).ConfigureAwait(false);
        return result;
    }
}
