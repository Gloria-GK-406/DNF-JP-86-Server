using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Blacklist;

internal static class BlacklistPayloadCodec
{
    /// <summary>
    /// The longest name a request is read with: the most the 0x0076 reply can carry back. A target is
    /// found by exact name, so a found target's name is never longer than this either.
    /// </summary>
    public const int RequestNameLimit = RegisterBlacklistResponse.NameLimit;

    // The 0x0076 handler's prompts (VA 0111F6F9 onward): 74 already listed, 76 no such name, 78 list
    // full. Any other reason, 0 included, is shown nothing.
    private const byte DuplicateReason = 74;
    private const byte NoSuchCharacterReason = 76;
    private const byte FullReason = 78;
    private const byte SilentReason = 0;

    /// <summary>The only reason the 0x0077 handler shows (VA <c>011244EC</c>): the target is not on the list.</summary>
    private const byte NotListedReason = 75;

    /// <summary>The body is exactly one counted name; anything else has no name.</summary>
    public static byte[]? DecodeName(ReadOnlySpan<byte> body) =>
        CharacterNameField.TryRead(body, offset: 0, out var name)
            && name.Length <= RequestNameLimit
            && CharacterNameField.LengthPrefixSize + name.Length == body.Length
            ? name
            : null;

    public static RegisterBlacklistResult ResultOf(CharacterRelationAddOutcome outcome) => outcome switch
    {
        CharacterRelationAddOutcome.Added => RegisterBlacklistResult.Registered,
        CharacterRelationAddOutcome.AlreadyPresent => RegisterBlacklistResult.Duplicate,
        CharacterRelationAddOutcome.TargetMissing => RegisterBlacklistResult.NoSuchCharacter,
        CharacterRelationAddOutcome.Full => RegisterBlacklistResult.Full,
        CharacterRelationAddOutcome.Self => RegisterBlacklistResult.Refused,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a relation add outcome."),
    };

    public static byte[] Encode(RegisterBlacklistResponse response) => response.Result switch
    {
        RegisterBlacklistResult.Registered => GameCmdEnvelope.Accept(Counted(response.Name, RegisterBlacklistResponse.NameLimit)),
        RegisterBlacklistResult.Duplicate => GameCmdEnvelope.Refuse(DuplicateReason),
        RegisterBlacklistResult.NoSuchCharacter => GameCmdEnvelope.Refuse(NoSuchCharacterReason),
        RegisterBlacklistResult.Full => GameCmdEnvelope.Refuse(FullReason),
        RegisterBlacklistResult.Refused => GameCmdEnvelope.Refuse(SilentReason),
        _ => throw new ArgumentOutOfRangeException(nameof(response), response.Result, "Not a blacklist result."),
    };

    public static byte[] Encode(DeleteBlacklistResponse response) => response.Name is { } name
        ? GameCmdEnvelope.Accept(Counted(name, DeleteBlacklistResponse.NameLimit))
        : GameCmdEnvelope.Refuse(NotListedReason);

    private static byte[] Counted(byte[] name, int limit)
    {
        ChatDString.Require(name, 1, limit, "blacklist reply name");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        ChatDString.Write(writer, name);
        writer.Flush();
        return stream.ToArray();
    }
}

internal sealed class RegisterBlacklistDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new RegisterBlacklistRequest(metadata, BlacklistPayloadCodec.DecodeName(body));
}

internal sealed class DeleteBlacklistDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new DeleteBlacklistRequest(metadata, BlacklistPayloadCodec.DecodeName(body));
}

internal sealed class BlacklistListDecoder : IGameMessageDecoder
{
    // The request has no fields, so nothing in a body could change the answer; it is not read.
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new BlacklistListRequest(metadata);
}

internal sealed class RegisterBlacklistResponseEncoder : GameCmdMessageEncoder<RegisterBlacklistResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RegisterBlacklistResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(RegisterBlacklistResponse message) => BlacklistPayloadCodec.Encode(message);
}

internal sealed class DeleteBlacklistResponseEncoder : GameCmdMessageEncoder<DeleteBlacklistResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(DeleteBlacklistResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(DeleteBlacklistResponse message) => BlacklistPayloadCodec.Encode(message);
}
