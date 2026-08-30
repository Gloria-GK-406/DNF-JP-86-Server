using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>
/// A compensation service that never compensates.
/// </summary>
/// <remarks>
/// The first phase's implementation of the seam. It answers the rates it was given, unchanged, so
/// that everything upstream of it is written against the real shape from the first day and the
/// day something is put behind it nothing here moves -- including these tests.
/// </remarks>
internal sealed class NoDropRateAdjustment : IDropRateAdjuster
{
    public DropRates Adjust(CharacterId characterId, DropRates rates) => rates;
}
