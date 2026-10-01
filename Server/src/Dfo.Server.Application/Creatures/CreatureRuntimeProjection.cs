using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Creatures;

internal static class CreatureRuntimeProjection
{
    public static CharacterPetRuntimeState Build(CreatureState creature) => new(
        creature.ItemUid, creature.Stomach, checked((byte)creature.ModeFlag),
        creature.Experience, creature.Mode1Field0A, creature.Mode1Field0B,
        creature.Level, [.. creature.NameBytes], checked((byte)creature.TailFlag));
}
