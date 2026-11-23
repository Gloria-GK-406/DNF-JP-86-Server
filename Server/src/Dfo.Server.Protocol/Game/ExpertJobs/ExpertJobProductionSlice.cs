using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.ExpertJobs;

internal sealed record ExpertJobProductionRequest(GameMessageMetadata Metadata, ClientMessageType MessageType,
    int RecipeId = 0, ushort Attempts = 0, short CardSlot = -1, ExpertJobType ExtractorType = ExpertJobType.None,
    byte TargetList = 0, short ExtractorSlot = -1, short TargetSlot = -1, bool Malformed = false) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public bool IsExtraction => MessageType == ClientMessageType.GameExpertJobExtraction;
}

internal sealed class ExpertJobProductionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var type = (ClientMessageType)(0x00010000 | metadata.Type);
        return type switch
        {
            ClientMessageType.GameExpertJobCraft when body.Length == 8 =>
                new ExpertJobProductionRequest(metadata, type, BinaryPrimitives.ReadInt32LittleEndian(body),
                    BinaryPrimitives.ReadUInt16LittleEndian(body[4..]), BinaryPrimitives.ReadInt16LittleEndian(body[6..])),
            ClientMessageType.GameExpertJobExtraction when body.Length == 6 =>
                new ExpertJobProductionRequest(metadata, type, ExtractorType: (ExpertJobType)body[0],
                    TargetList: body[3], ExtractorSlot: BinaryPrimitives.ReadInt16LittleEndian(body[1..]),
                    TargetSlot: BinaryPrimitives.ReadInt16LittleEndian(body[4..])),
            _ => new ExpertJobProductionRequest(metadata, type, Malformed: true),
        };
    }
}

internal sealed record ExpertJobProductionReply(ServerMessageType MessageType, byte Error,
    ExpertJobCraftResult? Craft = null, short TargetSlot = -1, ExpertJobProductionReceipt? Extraction = null) : IGameCmdMessage;

internal sealed class ExpertJobProductionEncoder : GameCmdMessageEncoder<ExpertJobProductionReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ExpertJobProductionReply message)
    {
        if (message.Error != 0) return OnePayload(message, new byte[] { 0, message.Error });
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write((byte)1);
        if (message.Craft is { AttemptedOutput: { } output } craft)
        {
            writer.Write((byte)1);
            writer.Write(output.ItemId);
            writer.Write(output.Count);
            writer.Write(craft.Successes);
            writer.Write(craft.Failures);
            writer.Write((byte)0);
        }
        else if (message.Extraction is { } extraction)
        {
            writer.Write((byte)InventoryContainerType.Main);
            writer.Write(message.TargetSlot);
            writer.Write(checked((byte)extraction.Rewards.Count));
            foreach (var reward in extraction.Rewards)
            {
                var slot = A21InventoryLayout.ToGameSlot(reward.Slot
                    ?? throw new InvalidDataException("Expert job output has no inventory slot."));
                writer.Write(checked((short)slot.Index));
                writer.Write(reward.ItemId);
                writer.Write(reward.Count);
            }
        }
        else throw new InvalidDataException("Successful expert job reply has no result.");
        return OnePayload(message, body.ToArray());
    }
}

internal sealed record ExpertJobProductionCompleted(SelectedSessionIdentity Actor, ExpertJobProductionReply Reply,
    ExpertJobProductionReceipt Receipt) : INotification<ExpertJobProductionCompleted>;

internal sealed class ExpertJobProductionCompletedHandler(ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views) : INotificationHandler<ExpertJobProductionCompleted>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ExpertJobProductionCompleted completed, CancellationToken ct)
    {
        if (recipient.ExpectedSession != completed.Actor || !sessions.IsInWorld(completed.Actor)) return [];
        var receipt = completed.Receipt;
        var reply = completed.Reply;
        var fullInventory = reply.Craft?.ExtractorProduct == true;
        var userInfo = receipt.ExperienceChanged || reply.Extraction is not null;
        var jobInfo = receipt.LevelChanged || fullInventory;
        var view = fullInventory || userInfo || jobInfo
            ? await views.TryLoadAsync(completed.Actor, ct).ConfigureAwait(false) : null;
        var frames = new List<IServerNetworkMessage> { reply };
        if (fullInventory && view is not null)
        {
            var main = view.CharacterInventory.Containers.Single(container => container.ContainerType == InventoryContainerType.Main);
            frames.Add(new InventoryFullSnapshotMessage(main, view.CharacterInventory, view.AccountInventory, receipt.PreparedAt));
        }
        else
        {
            foreach (var replaced in receipt.ReplacedSlots.Select(A21InventoryLayout.ToGameSlot))
                frames.Add(GameInventoryDelivery.ClearedSlotNotification(replaced.Space, replaced.Index, receipt.PreparedAt));
            foreach (var group in receipt.InventoryChanges.Select(change => (Slot: A21InventoryLayout.ToGameSlot(change.Slot), change.Core))
                         .GroupBy(change => change.Slot.Space))
            {
                foreach (var item in group.Where(change => change.Core is null))
                    frames.Add(GameInventoryDelivery.ClearedSlotNotification(group.Key, item.Slot.Index, receipt.PreparedAt));
                var updated = group.Where(change => change.Core is not null)
                    .Select(change => new InventoryItemState(change.Core!.ItemUid, change.Slot.Index, change.Core.ToCore())).ToArray();
                if (updated.Length != 0)
                    frames.Add(GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key, updated, receipt.PreparedAt)));
            }
            if (receipt.Gold is { } gold)
                frames.Add(GameInventoryDelivery.CarriedGoldNotification(gold, receipt.PreparedAt));
        }
        if (view is not null && userInfo)
        {
            var equipment = view.CharacterInventory.Containers.SingleOrDefault(container => container.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
            frames.Add(new CurrentUserStateMessage(view.Character, view.CharacterEntry.Public, equipment, view.CharacterEntry.Features.Pets));
        }
        if (view is not null && jobInfo)
            frames.Add(new ExpertJobStateMessage(view.CharacterEntry.Features.ExpertJob));
        return frames;
    }
}

internal sealed record GameExpertJobProductionCommand(NetworkDispatchContext Dispatch, ExpertJobProductionRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameExpertJobProductionCommandHandler(ISelectedSessionDomainService sessions, IExpertJobDomainService expertJobs,
    ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox)
    : ICommandHandler<GameExpertJobProductionCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameExpertJobProductionCommand command,
        CancellationToken ct)
    {
        var input = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        var responseType = (ServerMessageType)input.MessageType;
        if (input.Malformed || execution.Request is not CommandRequestContext.ProtocolEffectSelected selected
            || !sessions.IsInWorld(selected.Recipient.Session) || selected.Recipient.Session.ConnectionId != connection)
            return await RejectAsync(ExpertJobProductionRejection.InvalidState).ConfigureAwait(false);
        var actor = selected.Recipient.Session;
        var role = expertJobs.Get(actor.CharacterId);

        if (input.IsExtraction)
        {
            BusinessSlot extractor;
            BusinessSlot target;
            if (input.TargetList != (byte)InventoryContainerType.Main)
                return await RejectAsync(ExpertJobProductionRejection.InvalidItem).ConfigureAwait(false);
            try
            {
                extractor = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, input.ExtractorSlot));
                target = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, input.TargetSlot));
            }
            catch (InventorySlotMappingException)
            {
                return await RejectAsync(ExpertJobProductionRejection.InvalidItem).ConfigureAwait(false);
            }
            var result = await units.WriteAsync(execution,
                new("expert-job-extract", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                async (unit, token) =>
                {
                    var service = await FindProductionAsync(role, input.ExtractorType, token).ConfigureAwait(false);
                    return service is null ? new ExpertJobExtractionResult(ExpertJobProductionRejection.InvalidState)
                        : await service.ExtractAsync(actor, extractor, target, unit.Footprint, token).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);
            if (result.Receipt is not { } receipt) return await RejectAsync(result.Rejection).ConfigureAwait(false);
            await PublishAsync(new(responseType, 0, TargetSlot: input.TargetSlot, Extraction: receipt), receipt).ConfigureAwait(false);
            return true;
        }

        if (input.RecipeId <= 0 || input.Attempts == 0 || input.CardSlot != -1)
            return await RejectAsync(ExpertJobProductionRejection.InvalidState).ConfigureAwait(false);
        var craft = await units.WriteAsync(execution,
            new("expert-job-craft", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                var state = await role.GetStateAsync(token).ConfigureAwait(false);
                var service = await FindProductionAsync(role, state.Type, token).ConfigureAwait(false);
                return service is null ? new ExpertJobCraftResult(ExpertJobProductionRejection.InvalidState)
                    : await service.CraftAsync(actor, input.RecipeId, input.Attempts, unit.Footprint, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        if (craft.Receipt is not { } crafted) return await RejectAsync(craft.Rejection).ConfigureAwait(false);
        await PublishAsync(new(responseType, 0, Craft: craft), crafted).ConfigureAwait(false);
        return true;

        async ValueTask<bool> RejectAsync(ExpertJobProductionRejection rejection)
        {
            var error = rejection switch
            {
                ExpertJobProductionRejection.InventoryFull => (byte)4,
                ExpertJobProductionRejection.InvalidItem => input.IsExtraction ? (byte)13 : (byte)19,
                ExpertJobProductionRejection.InvalidRecipe => (byte)17,
                ExpertJobProductionRejection.LevelTooLow => (byte)14,
                ExpertJobProductionRejection.InsufficientMaterials => (byte)21,
                ExpertJobProductionRejection.InsufficientGold => (byte)22,
                _ => (byte)19,
            };
            await outbox.RegisterCmdAsync(connection, new ExpertJobProductionReply(responseType, error), ct).ConfigureAwait(false);
            return false;
        }

        ValueTask<NotificationDelivery> PublishAsync(ExpertJobProductionReply reply, ExpertJobProductionReceipt receipt) =>
            projections.ProjectAsync(new NotificationAudience.TheseSessions([actor]),
                new ExpertJobProductionCompleted(actor, reply, receipt), ct);
    }

    private static async ValueTask<IExpertJobProductionService?> FindProductionAsync(ICharacterExpertJob role,
        ExpertJobType type, CancellationToken ct) => type switch
    {
        ExpertJobType.Alchemist => await role.GetAlchemistAsync(ct).ConfigureAwait(false),
        ExpertJobType.DollController => await role.GetDollControllerAsync(ct).ConfigureAwait(false),
        ExpertJobType.Enchanter => await role.GetEnchanterAsync(ct).ConfigureAwait(false),
        _ => null,
    };
}

internal sealed class ExpertJobProductionHandler(ICommandBus commands)
    : ClientMessageHandler<ExpertJobProductionRequest>, IProtocolCommandMessageHandler<GameExpertJobProductionCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ExpertJobProductionRequest message,
        CancellationToken ct) => _ = await commands.SendAsync(new GameExpertJobProductionCommand(context, message), ct).ConfigureAwait(false);
}

internal static class ExpertJobProductionRegistration
{
    internal static void AddExpertJobProductionSlice(this IServiceCollection services)
    {
        ClientMessageType[] messages = [ClientMessageType.GameExpertJobCraft, ClientMessageType.GameExpertJobExtraction];
        services.AddProtocolCommandHandler<GameExpertJobProductionCommand, bool, GameExpertJobProductionCommandHandler>(
            [CommandRegistrationExit.CommittedReplayCurrentStateBatch],
            [.. messages.Select(type => new ProtocolCommandMessageBinding(type, typeof(ExpertJobProductionRequest)))]);
        foreach (var type in messages)
        {
            services.AddGameCmdMessageDecoder<ExpertJobProductionDecoder>(type);
            services.RegisterProtocolMessageHandler<ExpertJobProductionHandler, ExpertJobProductionRequest, GameExpertJobProductionCommand>(type);
        }
        services.AddGameCmdMessageEncoder<ExpertJobProductionReply, ExpertJobProductionEncoder>();
        services.AddNotificationHandler<ExpertJobProductionCompleted, ExpertJobProductionCompletedHandler>();
    }
}
