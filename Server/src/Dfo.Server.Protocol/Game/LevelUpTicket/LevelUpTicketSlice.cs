using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.LevelUpTicket;

public sealed record LevelUpTicketRequest(GameMessageMetadata Metadata, short SourceSlot, byte Reserved, bool Complete)
    : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameLevelUpTicket;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Experience
        | ReplayProjectionNeeds.Skills | ReplayProjectionNeeds.Quests;
}

public sealed record LevelUpTicketResponse(short SourceSlot, byte? ErrorCode = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLevelUpTicket;
}

internal sealed class LevelUpTicketDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new LevelUpTicketRequest(metadata, body.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(body) : (short)-1,
            body.Length >= 3 ? body[2] : (byte)0, body.Length == 3);
}

internal static class LevelUpTicketPayloadCodec
{
    public static byte[] Encode(LevelUpTicketResponse response)
    {
        if (response.ErrorCode is { } error) return [0, error];
        // A21's nonzero-result branch also opens the old event-server migration
        // window for level >= 60 (D2907E..D290A2). Zero result with reason zero
        // takes the silent completion path (D290DF -> D29140 -> D29158), which
        // clears the wait without a popup or local decrement. Authoritative
        // inventory, experience and quest NOTIs follow the committed receipt.
        return [0, 0];
    }
}
internal sealed class LevelUpTicketEncoder : GameCmdMessageEncoder<LevelUpTicketResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(LevelUpTicketResponse message) =>
        OnePayload(message, LevelUpTicketPayloadCodec.Encode(message));
}

public sealed record GameLevelUpTicketCommand(NetworkDispatchContext Dispatch,LevelUpTicketRequest Input):IProtocolCommand<bool>
{
    public IClientNetworkMessage Message=>Input;
    public ProtocolFrameMetadata Frame=>new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameLevelUpTicketCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ILevelUpTicketDomainService tickets, TimeProvider clock, ICommandWorkUnits units, ICommandProjections notifications, IGameCommandOutbox outbox,
    ISelectedCharacterCurrentViewDomainService views, IQuestOfferRefreshDomainService offers,
    ICharacterQuestLogDomainService quests, IDungeonRunQueryDomainService runs, ILogger<GameLevelUpTicketCommandHandler> logger,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService housing)
    : ICommandHandler<GameLevelUpTicketCommand,bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameLevelUpTicketCommand command, CancellationToken ct)
    {
        var request=context.Request ?? throw new InvalidOperationException("Level ticket requires its explicit request.");
        var dispatch=command.Dispatch;
        var message=command.Input;
        if (!message.Complete || message.Reserved != 0 || message.SourceSlot < 0)
        {
            await RefuseAsync(dispatch, message, 1, ct).ConfigureAwait(false);
            return true;
        }
        var session = sessions.FindByConnection(dispatch.Connection.ConnectionId);
        if (session is null
            || !onlineUsers.TryGetByConnection(dispatch.Connection.ConnectionId, out var online) || online is null)
        {
            await RefuseAsync(dispatch, message, 1, ct).ConfigureAwait(false);
            return true;
        }
        LevelUpTicketResult result;
        if(request is CommandRequestContext.ProtocolEffectRejected) { await RefuseAsync(dispatch,message,1,ct).ConfigureAwait(false); return true; }
        if(request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != session)
            throw new InvalidOperationException("Level ticket requires its originally selected actor.");
        var outcome=await units.WriteResultAsync(context,new("LevelUpTicket",[selected.Recipient],CommandWorkUnitKind.MainSettlement),async (unit,token) =>
        {
            var footprint=unit.Footprint;
            try { return new ActionOutcome(await tickets.UseAsync(session,OpeningWireSlots.FromMain(message.SourceSlot),clock.GetUtcNow(),footprint,token).ConfigureAwait(false),null); }
            catch(Exception failure) when(failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
            { unit.RequireRollback(failure); return new ActionOutcome(null,failure); }
        },ct).ConfigureAwait(false);
        Exception? error=outcome switch {
            CommandWriteResult<ActionOutcome>.Returned {Value:{Failure:{} failure}} => failure,
            CommandWriteResult<ActionOutcome>.CommitRolledBack rollback => rollback.Cause,
            _=>null,
        };
        if(error is not null)
        {
            byte code;
            if(error is OpeningRejectedException rejected)
            {
                code=rejected.Reason switch { OpeningRejection.WrongItem or OpeningRejection.MissingMaterial=>0x11,OpeningRejection.ItemUnusable=>0x16,OpeningRejection.UnsupportedContent=>0x17,_=>(byte)1 };
                logger.LogInformation("Level-up ticket refused character={CharacterId} slot={Slot}: {Reason}",session.CharacterId,message.SourceSlot,error.Message);
            }
            else { code=0x63; logger.LogError(error,"Level-up ticket transaction failed character={CharacterId}",session.CharacterId); }
            await RefuseAsync(dispatch,message,code,ct).ConfigureAwait(false); return true;
        }
        result=outcome is CommandWriteResult<ActionOutcome>.Returned {Value:{Result:{} value}} ? value
            : throw new InvalidOperationException("Level ticket returned no action outcome.");
        var following = new List<IServerNetworkMessage>
        {
            new ExperienceNotification(result.Progress.Level, result.Progress.Experience, result.Points,
                AccountHonorLevel.Initial, ExperienceBonusLines.None),
        };
        try
        {
            var log = quests.LogOf(session.AccountId, session.CharacterId);
            following.AddRange(QuestClearCouponProjection.FromLog(await log.ClearedAsync(ct).ConfigureAwait(false),
                await log.CarriedAsync(ct).ConfigureAwait(false)));
            var current = await views.TryLoadAsync(session, ct).ConfigureAwait(false);
            if (current is not null)
            {
                current = await offers.RefreshAsync(current, session.AccountId, session.CharacterId, result.Progress.Level, ct).ConfigureAwait(false);
                following.Add(new AvailableQuestListMessage(result.Progress.Level, current.World.Content.AvailableQuests));
                var equipment = current.CharacterInventory.Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items;
                if (runs.Find(session.CharacterId) is null)
                    following.Add(new CurrentUserStateMessage(current.Character, current.CharacterEntry.Public, equipment, current.CharacterEntry.Features.Pets));
                following.Add(new ExtendedUserStateMessage(current.Character, current.CharacterEntry.Public,
                    current.CharacterEntry.Skills, equipment, result.Inventory.PreparedAt));
                following.Add(new SkillStateMessage(current.CharacterEntry.Skills));
                following.AddRange(await HousingCurrentProjection.ReadAsync(housing, session, ct).ConfigureAwait(false));
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogError(failure, "Level-up ticket committed but auxiliary projection failed character={CharacterId}", session.CharacterId);
        }
        logger.LogInformation("Level-up ticket used character={CharacterId} slot={Slot} level={Level} exp={Exp} completedQuests={Count}",
            session.CharacterId, message.SourceSlot, result.Progress.Level, result.Progress.Experience, result.CompletedQuestIds.Length);
        await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new OpeningCompleted(result.Inventory, [new LevelUpTicketResponse(message.SourceSlot)], FollowingMessages: following), ct).ConfigureAwait(false);
        return true;
    }
    private sealed record ActionOutcome(LevelUpTicketResult? Result,Exception? Failure);

    private ValueTask RefuseAsync(NetworkDispatchContext dispatch, LevelUpTicketRequest request, byte error, CancellationToken ct) =>
        outbox.RegisterCmdAsync(dispatch.Connection.ConnectionId, new LevelUpTicketResponse(request.SourceSlot, error), ct);
}

public sealed class LevelUpTicketHandler(ICommandBus commands):ClientMessageHandler<LevelUpTicketRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameLevelUpTicketCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,LevelUpTicketRequest message,CancellationToken ct)=>
        _ = await commands.SendAsync(new GameLevelUpTicketCommand(context,message),ct).ConfigureAwait(false);
}
