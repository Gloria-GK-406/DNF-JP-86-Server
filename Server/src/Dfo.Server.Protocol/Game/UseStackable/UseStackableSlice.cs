using Dfo.Server.Abstractions.Characters;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.UseStackable;

/// <summary>
/// CMD 0x002C <c>USE_STACKABLE</c>: the player consuming one item off a pile.
/// </summary>
/// <remarks>
/// Observed live on 2026-08-18: a potion drunk from quick slot 3 in the tutorial. The client
/// applies the recovery itself and then asks the server to take one off the pile, so an
/// unanswered request looks like a potion that heals forever -- which is what the session showed
/// before this existed.
///
/// The static census marks the body's layout only partly derivable, because the send path goes
/// through a virtual call whose payload depends on the runtime item type. The prefix the reference
/// server reads is fixed regardless, and the live frame matches it field for field.
/// </remarks>
public sealed record UseStackableRequest(
    GameMessageMetadata Metadata,
    GameSlot Slot,
    int InstanceValue,
    int ItemCode) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameUseStackable;
}

/// <summary>The CMD acknowledgement for the same type.</summary>
public sealed record UseStackableResponse(
    byte? ErrorCode,
    GameSlot Slot,
    int InstanceValue,
    int ItemCode) : IGameCmdMessage
{
    /// <summary>
    /// The client's own word for each refusal, read out of its string table rather than guessed.
    /// </summary>
    /// <remarks>
    /// The table is archived at
    /// <c>protocol/patterns/data/probes/cmd-reason-messages-20260818.md</c>. Its point for this
    /// type is that the client can already say four different things, so answering everything with
    /// one code throws away information the player could act on.
    /// <para>
    /// Cooldown and place share <c>19</c> because the client has no sentence for either of them
    /// separately; "目前无法使用物品" is the closest true thing it can say. Fatigue's <c>95</c> is
    /// left alone -- this server has no fatigue model, and a code for a rule that is not enforced
    /// would be a lie in the other direction.
    /// </para>
    /// </remarks>
    public const byte NotConsumable = 23;

    /// <summary>「Lv过低， 无法使用该物品」</summary>
    public const byte LevelTooLow = 14;

    /// <summary>「该物品与当前职业不符， 无法使用」</summary>
    public const byte JobMismatch = 8;

    /// <summary>「目前无法使用物品」 -- the cooldown and the place both land here.</summary>
    public const byte CurrentlyUnusable = 19;

    /// <summary>
    /// A code the client shows nothing for.
    /// </summary>
    /// <remarks>
    /// It appears in neither this type's branch chain nor the shared fallback table every handler
    /// consults, so the frame arrives and no text is drawn. That is the honest answer when the
    /// player did nothing wrong -- a lost race, a server fault -- because every sentence the client
    /// owns would blame them for it.
    /// </remarks>
    public const byte Silent = 2;

    public ServerMessageType MessageType => ServerMessageType.GameUseStackable;

    public static UseStackableResponse Success(
        GameSlot slot, int instanceValue, int itemCode) =>
        new(null, slot, instanceValue, itemCode);

    public static UseStackableResponse Failure(
        GameSlot slot, int instanceValue, int itemCode, byte errorCode) =>
        new(errorCode, slot, instanceValue, itemCode);
}

internal static class UseStackablePayloadCodec
{
    /// <summary>
    /// The shortest body the reference server will parse. The item identifier past offset 7 is
    /// read only when the body carries it; a shorter frame names the slot and leaves the server to
    /// resolve what is in it.
    /// </summary>
    public const int MinimumBodyLength = 7;

    private const int ItemCodeBodyLength = 11;

    public static byte[] Encode(UseStackableResponse message)
    {
        // A refusal has no slot: the reference builder writes the code where the success writes
        // the slot, so the two bodies differ in length as well as in content.
        if (message.ErrorCode is { } errorCode)
        {
            var failure = new byte[11];
            failure[0] = 0x00;
            failure[1] = errorCode;
            failure[2] = (byte)message.Slot.Space;
            BinaryPrimitives.WriteInt32LittleEndian(failure.AsSpan(3, 4), message.InstanceValue);
            BinaryPrimitives.WriteInt32LittleEndian(failure.AsSpan(7, 4), message.ItemCode);
            return failure;
        }

        var body = new byte[12];
        body[0] = 0x01;
        BinaryPrimitives.WriteInt16LittleEndian(
            body.AsSpan(1, 2), checked((short)message.Slot.Index));
        body[3] = (byte)message.Slot.Space;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), message.InstanceValue);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8, 4), message.ItemCode);
        return body;
    }

    public static UseStackableRequest Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new(metadata,
            new GameSlot(
                (InventoryContainerType)body[2],
                BinaryPrimitives.ReadInt16LittleEndian(body)),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            body.Length >= ItemCodeBodyLength ? BinaryPrimitives.ReadInt32LittleEndian(body[7..]) : 0);
}

internal sealed class UseStackableDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < UseStackablePayloadCodec.MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"USE_STACKABLE requires at least {UseStackablePayloadCodec.MinimumBodyLength} "
                    + $"body bytes; received '{body.Length}'.");
        }

        return UseStackablePayloadCodec.Decode(metadata, body);
    }
}

internal sealed class UseStackableResponseEncoder : GameCmdMessageEncoder<UseStackableResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UseStackableResponse message) =>
        OnePayload(GameProtocol.UseStackableType, UseStackablePayloadCodec.Encode(message));
}

/// <summary>Why a use did not happen, in the server's own terms.</summary>
/// <remarks>
/// Named separately from the client's codes because the two are not the same vocabulary. Several
/// of these refusals share a code, and two of them have no code at all; collapsing them at the
/// point of decision would leave the log unable to say which one occurred.
/// </remarks>
public enum ItemUseStatus
{
    Used = 0,

    /// <summary>No published session for the connection that asked.</summary>
    NoSession,

    /// <summary>The request named a list this server does not serve.</summary>
    ListNotServed,

    /// <summary>The slot is empty, or holds something other than what the client named.</summary>
    SlotDisagrees,

    /// <summary>The archive declares nothing usable under that identifier.</summary>
    NotAUsableItem,

    LevelTooLow,
    JobMismatch,
    OnCooldown,
    PlaceNotAllowed,

    /// <summary>The character is not bound to a main container.</summary>
    NoBoundContainer,

    /// <summary>The slot changed between the decision and the write.</summary>
    SlotChanged,

    /// <summary>The character's own state could not be read.</summary>
    CharacterStateUnavailable,
}

/// <summary>What a use did, or why it did not.</summary>
public sealed record ItemUseResult(ItemUseStatus Status, int ItemId, int RemainingCount)
{
    public static ItemUseResult Refused(ItemUseStatus status, int itemId = 0) =>
        new(status, itemId, 0);
}

/// <summary>One character using one item out of one slot.</summary>
public sealed record UseStackableCommand(
    CharacterId CharacterId,
    BusinessSlot Slot,
    int ItemCode) : ICommand<ItemUseResult>;

/// <summary>
/// Decides whether the item may be used, and applies what using it changes.
/// </summary>
/// <remarks>
/// Exactly one is taken off the pile when the item is consumed at all: the client draws the
/// recovery and decrements its own count before the answer arrives, so consuming any other number
/// would leave the two disagreeing about a slot.
///
/// The eighty-four items whose <c>[stackable type]</c> begins with <c>unlimited</c> are consumed
/// zero times, and that is expressed by their effect set containing no consumption rather than by
/// a test in front of the decrement.
///
/// The recovery itself is not the server's to send. The reference server's use path mutates the
/// inventory and nothing else -- health follows from the item's own effect, which the client has
/// already applied.
/// </remarks>
public sealed class UseStackableCommandHandler(
    IBusinessInventorySlotDomainService inventory,
    ICharacterDomainService characters,
    IItemUseEffectDomainService effects) : ICommandHandler<UseStackableCommand, ItemUseResult>
{
    public async ValueTask<ItemUseResult> HandleAsync(
        CommandExecutionContext context,
        UseStackableCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Slot.Space.IsCarriedItemSpace())
            return ItemUseResult.Refused(ItemUseStatus.ListNotServed);

        var slot = inventory.Find(command.CharacterId, command.Slot);
        if (slot is null)
            return ItemUseResult.Refused(ItemUseStatus.NoBoundContainer);

        var item = await slot.Container.ReadSlotAsync(slot.StorageIndex, cancellationToken)
            .ConfigureAwait(false);

        // The client names what it believes it is using. A slot that holds something else means the
        // two disagree about the inventory, and deleting from it anyway would resolve that
        // disagreement in favour of whichever one is wrong.
        if (item is null || command.ItemCode > 0 && item.ItemId != command.ItemCode)
            return ItemUseResult.Refused(ItemUseStatus.SlotDisagrees);

        var verdict = await characters.Get(command.CharacterId)
            .CanUseAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (verdict is not ItemUsability.Allowed)
            return ItemUseResult.Refused(StatusFor(verdict), item.ItemId);

        var planned = await effects.PlanAsync(item.ItemId, item.SlotIndex, cancellationToken)
            .ConfigureAwait(false);
        var applied = await effects
            .ApplyAsync(command.CharacterId, slot.Container, item, planned, cancellationToken)
            .ConfigureAwait(false);

        return new ItemUseResult(ItemUseStatus.Used, applied.ItemId, applied.RemainingCount);
    }

    /// <summary>
    /// The refusal the judgement gave, in this protocol's own terms.
    /// </summary>
    /// <remarks>
    /// Listed rather than defaulted, for the same reason the code translation is: a verdict nobody
    /// mapped would otherwise arrive as "not a usable item" and be read out to the player as
    /// 「非消耗品， 无法使用」 -- a sentence about the item, for a judgement the server never made.
    /// </remarks>
    internal static ItemUseStatus StatusFor(ItemUsability usability) => usability switch
    {
        ItemUsability.NotAUsableItem => ItemUseStatus.NotAUsableItem,
        ItemUsability.LevelTooLow => ItemUseStatus.LevelTooLow,
        ItemUsability.JobMismatch => ItemUseStatus.JobMismatch,
        ItemUsability.OnCooldown => ItemUseStatus.OnCooldown,
        ItemUsability.PlaceNotAllowed => ItemUseStatus.PlaceNotAllowed,
        ItemUsability.CharacterStateUnavailable => ItemUseStatus.CharacterStateUnavailable,
        ItemUsability.Allowed => throw new ArgumentOutOfRangeException(
            nameof(usability), usability, "An allowed use is not a refusal."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(usability), usability, "No refusal was decided for this verdict."),
    };
}

/// <summary>
/// Turns the request into a command and the command's answer into something the client can read.
/// </summary>
/// <remarks>
/// The refusal reason is decided once, inside the command, and translated here. The translation is
/// the only place that knows about client codes, and the decision is the only place that knows
/// about items -- which is why a refusal can gain a better sentence without the rules moving.
/// </remarks>
public sealed class UseStackableHandler(
    ISelectedSessionDomainService selectedSessions,
    IInventorySlotMappingRuleService slotMappings,
    ICommandBus commandBus,
    IGameMessageSender sender,
    ILogger<UseStackableHandler> logger) : ClientMessageHandler<UseStackableRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        UseStackableRequest message,
        CancellationToken cancellationToken)
    {
        var response = await ResolveAsync(context, message, cancellationToken).ConfigureAwait(false);
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([response], []),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<UseStackableResponse> ResolveAsync(
        NetworkDispatchContext context,
        UseStackableRequest message,
        CancellationToken cancellationToken)
    {
        var slot = slotMappings.ToBusinessSlot(message.Slot);
        var responseSlot = slotMappings.ToGameSlot(slot);
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Use stackable {ConnectionId}: no published session",
                context.Connection.ConnectionId);
            return UseStackableResponse.Failure(
                responseSlot,
                message.InstanceValue,
                message.ItemCode,
                UseStackableResponse.Silent);
        }

        var result = await commandBus.SendAsync(
            new UseStackableCommand(
                session.CharacterId, slot, message.ItemCode),
            cancellationToken).ConfigureAwait(false);

        if (result.Status is ItemUseStatus.Used)
        {
            logger.LogInformation(
                "Use stackable {ConnectionId} character={CharacterId}: used item {ItemId} from "
                    + "slot {Slot}, {Remaining} left",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                result.ItemId,
                responseSlot.Index,
                result.RemainingCount);
            return UseStackableResponse.Success(
                responseSlot, message.InstanceValue, result.ItemId);
        }

        logger.LogInformation(
            "Use stackable {ConnectionId} character={CharacterId}: slot {Slot} refused as {Status}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            responseSlot.Index,
            result.Status);
        return UseStackableResponse.Failure(
            responseSlot, message.InstanceValue, message.ItemCode, ErrorCodeFor(result.Status));
    }

    /// <summary>
    /// The client sentence that is true of this refusal, or silence when it owns none.
    /// </summary>
    /// <remarks>
    /// Silence is chosen over the nearest available sentence on purpose. A lost race, a missing
    /// binding, a character whose state has not been published yet -- these are the server's
    /// problems, and every code the client draws for this type tells the player they did something
    /// wrong.
    /// <para>
    /// Every refusal is listed. A fall-through would turn the next refusal someone adds into
    /// silence without anyone choosing it, and the test that walks this enum would stay green while
    /// the player stopped being told anything.
    /// </para>
    /// <para>
    /// Raising is not free: the dispatcher does not catch, so the plugin logs and rethrows and the
    /// player's connection drops. That is the right trade only because the raise is unreachable
    /// except through a coding mistake, and because the tests that walk these enums are what keep
    /// it unreachable. Those tests and this method are one mechanism; neither is worth keeping
    /// alone.
    /// </para>
    /// </remarks>
    internal static byte ErrorCodeFor(ItemUseStatus status) => status switch
    {
        ItemUseStatus.ListNotServed => UseStackableResponse.NotConsumable,
        ItemUseStatus.NotAUsableItem => UseStackableResponse.NotConsumable,
        ItemUseStatus.LevelTooLow => UseStackableResponse.LevelTooLow,
        ItemUseStatus.JobMismatch => UseStackableResponse.JobMismatch,
        ItemUseStatus.OnCooldown => UseStackableResponse.CurrentlyUnusable,
        ItemUseStatus.PlaceNotAllowed => UseStackableResponse.CurrentlyUnusable,
        ItemUseStatus.NoSession => UseStackableResponse.Silent,
        ItemUseStatus.NoBoundContainer => UseStackableResponse.Silent,
        ItemUseStatus.SlotDisagrees => UseStackableResponse.Silent,
        ItemUseStatus.SlotChanged => UseStackableResponse.Silent,
        ItemUseStatus.CharacterStateUnavailable => UseStackableResponse.Silent,
        ItemUseStatus.Used => throw new ArgumentOutOfRangeException(
            nameof(status), status, "A use that happened has no error code."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "No client code was decided for this refusal."),
    };
}
