using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ExpertJobs;

internal sealed record ExpertJobGiveUpRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameExpertJobGiveUp;
}

internal sealed class ExpertJobGiveUpDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.IsEmpty
        ? new ExpertJobGiveUpRequest(metadata)
        : throw new InvalidDataException("GIVEUP_EXPERT_JOB requires an empty body.");
}

internal sealed record ExpertJobGiveUpAcknowledgement(byte Error, int Gold = 0, byte Count = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameExpertJobGiveUp;
}

internal sealed class ExpertJobGiveUpEncoder : GameCmdMessageEncoder<ExpertJobGiveUpAcknowledgement>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ExpertJobGiveUpAcknowledgement message)
    {
        if (message.Error != 0) return OnePayload(message, new byte[] { 0, message.Error });
        var payload = new byte[6];
        payload[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), message.Gold);
        payload[5] = message.Count;
        return OnePayload(message, payload);
    }
}

internal sealed record GameExpertJobGiveUpCommand(NetworkDispatchContext Dispatch, ExpertJobGiveUpRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameExpertJobGiveUpCommandHandler(ISelectedSessionDomainService sessions,
    IExpertJobDomainService jobs, IExpertStoreDomainService stores, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ISelectedCharacterCurrentViewDomainService views, IQuestOfferRefreshDomainService offers,
    IBusinessInventorySlotDomainService inventory, TimeProvider clock, ILogger<GameExpertJobGiveUpCommandHandler> logger)
    : ICommandHandler<GameExpertJobGiveUpCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameExpertJobGiveUpCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (!sessions.TryGetInWorld(connection.ConnectionId, out var session) || session is null)
            return await RefuseAsync(connection.ConnectionId, 8, ct).ConfigureAwait(false);
        var actor = new SelectedSessionRecipient(session, connection, command.Input.Metadata);
        var operation = await units.WriteResultAsync(execution,
            new("give-up-expert-job", [actor], Footprint: CommandFootprintMode.NonRequest),
            async (unit, token) =>
            {
                try
                {
                    var result = await jobs.Get(session.CharacterId).GiveUpAsync(session, unit.Footprint, token).ConfigureAwait(false);
                    return new GiveUpOperation(result, null);
                }
                catch (Exception failure) when (failure is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    unit.RequireRollback(failure);
                    return new GiveUpOperation(null, failure);
                }
            }, ct).ConfigureAwait(false);
        if (operation is not CommandWriteResult<GiveUpOperation>.Returned { Value.Result: { } result })
        {
            var failure = operation switch
            {
                CommandWriteResult<GiveUpOperation>.Returned returned => returned.Value.Failure,
                CommandWriteResult<GiveUpOperation>.CommitRolledBack rolledBack => rolledBack.Cause,
                _ => null,
            };
            logger.LogWarning(failure, "Failed to commit expert-job giveup for {CharacterId}", session.CharacterId.Value);
            return await RefuseAsync(connection.ConnectionId, 1, ct).ConfigureAwait(false);
        }
        if (result.Rejection != ExpertJobGiveUpRejection.None)
            return await RefuseAsync(connection.ConnectionId,
                result.Rejection == ExpertJobGiveUpRejection.InsufficientGold ? (byte)21 : (byte)8, ct).ConfigureAwait(false);

        // Gameplay runs this committed closure on the same lane as the transaction.
        result = result with { StoreClosed = stores.CloseExact(session) };
        var view = await views.TryLoadAsync(session, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The committed expert-job giveup lost its selected character.");
        view = await offers.RefreshAsync(view, session.AccountId, session.CharacterId, view.Character.Progression.Level, ct).ConfigureAwait(false);
        var frames = new List<IGameServerMessage>();
        if (result.StoreClosed)
            frames.Add(new ExpertStoreClosedMessage(session.WireUserId));
        if (result.Effects is { } applied)
        {
            foreach (var change in applied.Changes<ContainerConsumptionChange>())
            {
                var slot = A21InventoryLayout.ToGameSlot(change.Slot);
                var bound = inventory.Find(session.CharacterId, change.Slot);
                var item = bound is null ? null : await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
                frames.Add(GameInventoryDelivery.Notification(new(slot.Space,
                    [new(item?.ItemUid, slot.Index, item?.Snapshot.ToCore() ?? StackableItemCore.Create(0, -1, 0))], clock.GetUtcNow())));
            }
            if (applied.Changes<GoldEffectChange>().Any())
                frames.Add(GameInventoryDelivery.Notification(new(InventoryContainerType.Main,
                    [new(null, 0, StackableItemCore.Create(0, 0, result.Gold))], clock.GetUtcNow())));
        }
        frames.Add(new SkillStateMessage(view.CharacterEntry.Skills));
        frames.Add(new ExpertJobStateMessage(view.CharacterEntry.Features.ExpertJob));
        frames.Add(new QuestVisibilityMessage(view.CharacterEntry.Quests.VisibilityFlags));
        frames.Add(new AcceptedQuestProgressMessage([.. view.CharacterEntry.Quests.ActiveQuests.OrderBy(quest => quest.Slot)
            .Select(quest => new AcceptedQuestProgressState(checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]));
        frames.Add(new AvailableQuestListMessage(view.Character.Progression.Level, view.World.Content.AvailableQuests));
        var equipment = view.CharacterInventory.Containers.SingleOrDefault(container => container.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
        frames.Add(new CurrentUserStateMessage(view.Character, view.CharacterEntry.Public, equipment, view.CharacterEntry.Features.Pets));
        frames.Add(new ExpertJobGiveUpAcknowledgement(0, result.Gold, result.GiveUpCount));
        await outbox.RegisterExplicitAsync(connection.ConnectionId, new(frames, []), ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> RefuseAsync(NetworkConnectionId connection, byte error, CancellationToken ct)
    {
        await outbox.RegisterCmdAsync(connection, new ExpertJobGiveUpAcknowledgement(error), ct).ConfigureAwait(false);
        return false;
    }

    private sealed record GiveUpOperation(ExpertJobGiveUpResult? Result, Exception? Failure);
}

internal sealed class ExpertJobGiveUpHandler(ICommandBus commands)
    : ClientMessageHandler<ExpertJobGiveUpRequest>, IProtocolCommandMessageHandler<GameExpertJobGiveUpCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ExpertJobGiveUpRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameExpertJobGiveUpCommand(context, message), ct).ConfigureAwait(false);
}

internal static class ExpertJobGiveUpRegistration
{
    internal static IServiceCollection AddExpertJobGiveUpSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<ExpertJobGiveUpDecoder>(ClientMessageType.GameExpertJobGiveUp);
        services.AddGameCmdMessageEncoder<ExpertJobGiveUpAcknowledgement, ExpertJobGiveUpEncoder>();
        services.AddProtocolCommandHandler<GameExpertJobGiveUpCommand, bool, GameExpertJobGiveUpCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameExpertJobGiveUp, typeof(ExpertJobGiveUpRequest)));
        services.RegisterProtocolMessageHandler<ExpertJobGiveUpHandler, ExpertJobGiveUpRequest, GameExpertJobGiveUpCommand>(ClientMessageType.GameExpertJobGiveUp);
        return services;
    }
}
