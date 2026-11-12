using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.SendChat;

/// <summary>
/// The single check a line -- or a request to open a conversation -- passes before anything is
/// delivered. Everything passes today; muting, block lists and rate limits belong here, and nowhere else.
/// </summary>
internal static class ChatPreSendChecks
{
    public static bool Passes(SelectedSessionIdentity speaker) => true;
}
