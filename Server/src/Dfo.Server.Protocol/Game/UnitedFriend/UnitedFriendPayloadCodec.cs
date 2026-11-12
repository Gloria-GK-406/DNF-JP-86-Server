using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.UnitedFriend;

internal static class UnitedFriendPayloadCodec
{
    /// <summary>
    /// The longest name a request is read with: the most a friend list entry can carry. A target is
    /// found by exact name, so a found target's name always fits the list.
    /// </summary>
    public const int RequestNameLimit = FriendListMessage.NameLimit;

    // The 0x0122 handler's prompts (jump table VA 011198E0): 4 list full, 7 another server group,
    // 0x15 no such character. Any other reason, 1 included, is shown nothing.
    private const byte FullReason = 0x04;
    private const byte OtherServerGroupReason = 0x07;
    private const byte NoSuchCharacterReason = 0x15;
    private const byte SilentReason = 0x01;

    /// <summary>The only reason the 0x0123 handler shows (VA <c>01119959</c>): not on the list, refreshed on reconnect.</summary>
    private const byte NotListedReason = 0x15;

    private const int UidSize = sizeof(ushort);

    /// <summary><c>u16 uid, u8 server, dstr name</c> and nothing after it; anything else has no target.</summary>
    public static FriendTarget? DecodeAdd(ReadOnlySpan<byte> body) =>
        body.Length < UidSize ? null : DecodeTarget(body[UidSize..]);

    /// <summary><c>u8 server, dstr name</c> and nothing after it; anything else has no target.</summary>
    public static FriendTarget? DecodeDelete(ReadOnlySpan<byte> body) => DecodeTarget(body);

    private static FriendTarget? DecodeTarget(ReadOnlySpan<byte> body) =>
        body.Length >= 1
            && CharacterNameField.TryRead(body, offset: 1, out var name)
            && name.Length <= RequestNameLimit
            && 1 + CharacterNameField.LengthPrefixSize + name.Length == body.Length
            ? new FriendTarget(body[0], name)
            : null;

    public static AddFriendResult ResultOf(CharacterRelationAddOutcome outcome) => outcome switch
    {
        // A repeat is a success: the client only asks for someone it does not hold, so a repeat
        // means its list is behind, and the full list that follows brings it up to date.
        CharacterRelationAddOutcome.Added or CharacterRelationAddOutcome.AlreadyPresent => AddFriendResult.Added,
        CharacterRelationAddOutcome.Full => AddFriendResult.Full,
        CharacterRelationAddOutcome.TargetMissing => AddFriendResult.NoSuchCharacter,
        CharacterRelationAddOutcome.Self => AddFriendResult.Refused,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a relation add outcome."),
    };

    public static byte[] Encode(AddFriendResponse response) => response.Result switch
    {
        AddFriendResult.Added => GameCmdEnvelope.Accept(),
        AddFriendResult.Full => GameCmdEnvelope.Refuse(FullReason),
        AddFriendResult.OtherServerGroup => GameCmdEnvelope.Refuse(OtherServerGroupReason),
        AddFriendResult.NoSuchCharacter => GameCmdEnvelope.Refuse(NoSuchCharacterReason),
        AddFriendResult.Refused => GameCmdEnvelope.Refuse(SilentReason),
        _ => throw new ArgumentOutOfRangeException(nameof(response), response.Result, "Not an add-friend result."),
    };

    public static byte[] Encode(DeleteFriendResponse response) =>
        response.Removed ? GameCmdEnvelope.Accept() : GameCmdEnvelope.Refuse(NotListedReason);
}

internal sealed class AddFriendDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new AddFriendRequest(metadata, UnitedFriendPayloadCodec.DecodeAdd(body));
}

internal sealed class DeleteFriendDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new DeleteFriendRequest(metadata, UnitedFriendPayloadCodec.DecodeDelete(body));
}

internal sealed class AddFriendResponseEncoder : GameCmdMessageEncoder<AddFriendResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AddFriendResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(AddFriendResponse message) => UnitedFriendPayloadCodec.Encode(message);
}

internal sealed class DeleteFriendResponseEncoder : GameCmdMessageEncoder<DeleteFriendResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DeleteFriendResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(DeleteFriendResponse message) => UnitedFriendPayloadCodec.Encode(message);
}
