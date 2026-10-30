using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

public sealed record GameTaggedIdReportCommand(NetworkDispatchContext Dispatch, TaggedIdReportRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameTaggedIdReportCommandHandler(
    IOnlineUserQueryDomainService onlineUsers, ISelectedSessionDomainService selectedSessions,
    ITutorialRewardDomainService rewards,
    ICharacterContainerDomainService containers,
    IItemDeliveryDomainService delivery,
    ITutorialCompletionDomainService tutorial,
    ICommandWorkUnits units, IGameCommandOutbox sender, ILogger<TaggedIdReportHandler> logger)
    : ICommandHandler<GameTaggedIdReportCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext execution, GameTaggedIdReportCommand command,
        CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        IReadOnlyList<TutorialRewardItem> declared = message.RewardFlag == 0
            ? []
            : await rewards.DeclaredForAsync(message.FlagIndex, cancellationToken).ConfigureAwait(false);

        // The acknowledgement reports what reached the inventory, not what the archive declares.
        // An item that was refused -- unknown to the archive, or its band already full -- is left
        // out, because naming it here would tell the client to draw something the server does not
        // hold.
        var session = selectedSessions.TryGetInWorld(context.Connection.ConnectionId, out var inWorld) ? inWorld : null;
        var granted = new List<GrantedTutorialItem>(declared.Count);
        TutorialRewardResult? rewardResult = null;
        if (session is null)
        {
            if (declared.Count > 0)
            {
                logger.LogWarning(
                    "Tagged id report {ConnectionId}: flag {Flag} gives {Declared} item(s) but no "
                        + "character is in the world, skipping",
                    context.Connection.ConnectionId, message.FlagIndex, declared.Count);
            }
        }
        else
        {
            // One command for the whole step, so its items settle together rather than one at a
            // time -- and so each placement sees the ones before it.
            rewardResult = await units.WriteAsync(execution, new("TaggedIdReport", [new(session, context.Connection, message.Metadata)]),
                (_, token) => GrantAsync(containers, delivery, tutorial, session.CharacterId,
                    [.. declared.Select(item => new DeclaredRewardItem(item.ItemId, item.Count))],
                    session.AccountId, message.CompletesTutorial, token), cancellationToken).ConfigureAwait(false);

            granted.AddRange(rewardResult.Placed.Select(item => new GrantedTutorialItem(
                checked((ushort)item.SlotIndex), (uint)item.ItemId, (uint)item.Count)));

            if (rewardResult.Placed.Count < declared.Count)
            {
                logger.LogWarning(
                    "Tagged id report {ConnectionId}: flag {Flag} declared {Declared} item(s) but "
                        + "only {Granted} reached the inventory",
                    context.Connection.ConnectionId, message.FlagIndex, declared.Count,
                    rewardResult.Placed.Count);
            }
        }

        logger.LogInformation(
            "Tagged id report {ConnectionId} {Attribution}: id={Id} flag={Flag} declared={Declared} granted={Granted}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            message.FlagIndex,
            message.RewardFlag,
            declared.Count,
            granted.Count);

        var responseFrames = new List<IGameServerMessage>
        {
            new TutorialFlagAcknowledgement(granted),
        };
        if (rewardResult?.AccountMaterials is { Count: > 0 } accountMaterials)
        {
            responseFrames.Add(GameInventoryDelivery.VirtualResourceNotification(
                [.. accountMaterials.Select(material => new InventoryRuntimeValueState(
                    material.SlotIndex,
                    material.ItemId,
                    material.Value))],
                DateTimeOffset.UtcNow));
        }

        await sender.RegisterExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery(responseFrames, []),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static async ValueTask<TutorialRewardResult> GrantAsync(
        ICharacterContainerDomainService containers,
        IItemDeliveryDomainService delivery,
        ITutorialCompletionDomainService tutorial,
        CharacterId characterId, IReadOnlyList<DeclaredRewardItem> declared,
        AccountId accountId, bool completeTutorial, CancellationToken cancellationToken)
    {
        // Completion and any reward commit together; a repeated completion grants nothing.
        if (completeTutorial && !await tutorial.CompleteOnceAsync(
                new(accountId, characterId), cancellationToken).ConfigureAwait(false))
            return new TutorialRewardResult([], []);

        var container = containers.FindContainer(characterId, InventoryContainerType.Main);
        if (container is null)
            throw new InvalidOperationException("Tutorial reward inventory is unavailable.");

        var placed = new List<PlacedRewardItem>(declared.Count);
        var accountChanges = new List<InventoryRuntimeValueState>();
        var receipt = await delivery.DeliverAsync(accountId, characterId,
            declared.Select(x => new ItemDeliveryGrant(x.ItemId, x.Count)).ToArray(), "教程奖励", cancellationToken, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
        foreach (var item in receipt.Inventory)
        {
            placed.Add(new(ItemGrantWireProjection.Index(item), item.Snapshot.ItemId, item.Count));
            if (ItemGrantWireProjection.Shared(item) is { } shared) accountChanges.Add(shared);
        }

        return new TutorialRewardResult(placed, accountChanges);
    }
}
