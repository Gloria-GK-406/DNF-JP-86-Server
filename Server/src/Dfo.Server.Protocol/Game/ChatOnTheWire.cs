using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

// Shared by the chat slices (SendChat, OneToOneChat): the string every chat frame carries and the
// speaker checks a line or a conversation request passes before anything is delivered.

/// <summary>The <c>i32</c>-counted byte string every chat frame uses, written as the bytes arrived.</summary>
internal static class ChatDString
{
    /// <summary>The longest character name a chat request is decoded with; character names are far shorter.</summary>
    public const int DecodedNameLimit = 64;

    /// <summary>
    /// The fan-out bound. A frame over the client's limit would be dropped by every recipient, and
    /// an unbounded one could fill a third party's send queue, so it is refused before it is built.
    /// </summary>
    public static void Require(byte[] value, int minimum, int maximum, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length < minimum || value.Length > maximum)
            throw new InvalidDataException($"The {field} is {value.Length} bytes; the client takes {minimum} to {maximum}.");
    }

    public static void Write(BinaryWriter writer, byte[] value)
    {
        writer.Write(value.Length);
        writer.Write(value);
    }
}

/// <summary>
/// The speaker checks a line -- or a request to open a conversation -- passes before anything is
/// delivered: only those that do not depend on who receives it, such as muting and rate limits.
/// Everything passes today.
/// </summary>
/// <remarks>
/// A blacklist is not one of them. It is decided per recipient, so it is applied where the line is
/// delivered (the recipient filter in the chat command) and where a conversation is opened.
/// </remarks>
internal static class ChatPreSendChecks
{
    public static bool Passes(SelectedSessionIdentity speaker) => true;
}
