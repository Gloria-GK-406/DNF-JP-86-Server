namespace Dfo.Server.Networking.Framing;

/// <summary>
/// Channel endpoint inbound message numbers, mirroring <see cref="GameProtocol"/>.
/// </summary>
/// <remarks>
/// These values coincide with the matching <c>ClientMessageType</c> members because the Channel
/// endpoint numbers its messages exactly as the server catalogues them. They are restated here
/// anyway: the wire number is what the client sends, the catalogue member is what the server
/// decides, and the two spaces stay separate even where their values agree.
/// </remarks>
internal static class ChannelProtocol
{
    public const byte ScriptVersionMessageNumber = 0x05;

    public const byte ScriptMessageNumber = 0x09;

    public const byte ConnectMessageNumber = 0x0B;

    public const byte InfoMessageNumber = 0x11;
}
