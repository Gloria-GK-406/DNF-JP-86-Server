using Dfo.Server.Abstractions.Relations;

namespace Dfo.Server.Application.Relations;

internal sealed class CharacterRelationRuleService : ICharacterRelationRuleService
{
    /// <summary>The client's friend and blacklist windows both hold 64 (research/client-evidence.md, RQ-002); the only place the number is kept.</summary>
    private const int ClientListCapacity = 64;

    public int Capacity(CharacterRelationKind kind) => kind switch
    {
        CharacterRelationKind.Friend or CharacterRelationKind.Blacklist => ClientListCapacity,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a relation kind."),
    };
}
