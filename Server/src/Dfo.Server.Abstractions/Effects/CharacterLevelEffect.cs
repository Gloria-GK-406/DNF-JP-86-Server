using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Effects;

public sealed record SetCharacterLevelEffect(CharacterProgressionState Expected, byte TargetLevel) : DomainEffect;
public sealed record CharacterLevelChange(CharacterProgressionState Progress) : EffectChange;
