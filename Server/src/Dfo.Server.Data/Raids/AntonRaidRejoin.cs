using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Raids;

public readonly record struct AntonRaidRejoinOffer(uint Number, ushort Channel);
public readonly record struct AntonRaidRejoinExpiry(Guid Instance, uint Number,
    SelectedSessionIdentity DepartedSession, DateTimeOffset At);
