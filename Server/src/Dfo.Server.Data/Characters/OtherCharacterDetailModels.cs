using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Characters;

public enum CharacterDetailQueryFailure
{
    None,
    InvalidTarget,
    RequesterNotCurrent,
    TargetNotCurrent,
    DifferentListener,
    TargetRuntimeUnavailable,
}

/// <summary>
/// The exact requester/target generations and target runtime captured for one online inspect.
/// </summary>
public sealed record OtherCharacterDetailProjection(
    SelectedSessionIdentity Requester,
    SelectedSessionIdentity Target,
    SelectCharacterResult TargetState,
    DateTimeOffset ProjectedAt);

public sealed record CharacterDetailQueryResult(
    OtherCharacterDetailProjection? Projection,
    CharacterDetailQueryFailure Failure)
{
    public bool Succeeded => Projection is not null && Failure == CharacterDetailQueryFailure.None;

    public static CharacterDetailQueryResult Success(OtherCharacterDetailProjection projection) =>
        new(projection, CharacterDetailQueryFailure.None);

    public static CharacterDetailQueryResult Reject(CharacterDetailQueryFailure failure) =>
        failure == CharacterDetailQueryFailure.None
            ? throw new ArgumentOutOfRangeException(nameof(failure))
            : new(null, failure);
}
