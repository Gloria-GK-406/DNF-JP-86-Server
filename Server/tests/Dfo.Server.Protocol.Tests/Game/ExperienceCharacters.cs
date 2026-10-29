using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Game;

internal abstract class ExperienceCharacters() : OnePlayingCharacter(null!), ICharacterExperienceAwardDomainService
{
    public override ICharacter Get(CharacterId id) => new CarriedGold(0);
    public ValueTask<ExperienceAward?> AwardAsync(ExperienceCalculationContext source, DateTimeOffset at,
        SelectedSessionIdentity? session, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct) =>
        AddExpAsync(source, at, session, footprint, ct);
    public abstract ValueTask<ExperienceAward?> AddExpAsync(ExperienceCalculationContext source,
        DateTimeOffset at, SelectedSessionIdentity? session, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct);
}
