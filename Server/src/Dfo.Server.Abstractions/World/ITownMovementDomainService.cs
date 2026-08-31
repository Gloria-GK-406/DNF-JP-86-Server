using Dfo.Server.Data.Channel;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Whether a character may stand where they say they are standing.
/// </summary>
/// <remarks>
/// Two questions, asked in this order because they refuse for different reasons and a caller has a
/// different thing to say about each. Both are content questions, and the rules that answer them
/// are reached from here rather than from whatever is handling the frame.
/// </remarks>
public interface ITownMovementDomainService
{
    /// <summary>
    /// Whether the content declares this area of this town at all.
    /// </summary>
    /// <remarks>
    /// Asked first, and a caller refuses rather than clamps. The byte the entry acknowledgement
    /// carries is a lookup key the client dereferences without a null check, so storing a town the
    /// content does not declare turns the next login into a crash instead of a refusal here.
    /// </remarks>
    ValueTask<bool> DeclaresAreaAsync(int townId, int areaId, CancellationToken cancellationToken);

    /// <summary>Whether this channel serves that town.</summary>
    ValueTask<bool> CanEnterTownAsync(
        ChannelEntryIdentity channel, int townId, CancellationToken cancellationToken);
}
