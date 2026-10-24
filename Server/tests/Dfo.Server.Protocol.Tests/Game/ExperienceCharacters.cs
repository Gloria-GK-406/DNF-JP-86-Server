using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Game;

internal abstract class ExperienceCharacters() : OnePlayingCharacter(null!)
{
    public override ICharacter Get(CharacterId id) => new AwardCharacter(this);
    public abstract ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source,
        DateTimeOffset at, SelectedSessionIdentity? session, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct);
    private sealed class AwardCharacter(ExperienceCharacters owner) : CarriedGold(0)
    {
        public override ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source,
            DateTimeOffset at, SelectedSessionIdentity? session, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct) => owner.AddExpAsync(source, at, session, footprint, ct);
    }
}
