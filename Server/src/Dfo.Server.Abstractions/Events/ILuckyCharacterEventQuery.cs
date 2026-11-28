using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Events;

public interface ILuckyCharacterEventQuery
{
    ValueTask<LuckyCharacterPeriod> ReadAtAsync(DateTimeOffset at, CancellationToken cancellationToken);
}

public interface ILuckyCharacterBurning
{
    bool IsEnabled(CharacterId character, long revision);
    bool TrySet(SelectedSessionIdentity session, long revision, bool enabled);
}

public interface IFatigueBurningDungeonContentSource
{
    IReadOnlySet<int> LoadProhibitedDungeons();
}
