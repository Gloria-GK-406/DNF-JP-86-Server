using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Networking.Framing;

internal sealed class SelectCharacterProjectionComposer : ISelectCharacterProjectionComposer
{
    public IReadOnlyList<byte[]> Compose(
        IReadOnlyList<ISelectCharacterSupplementalProjection> projections)
    {
        ArgumentNullException.ThrowIfNull(projections);
        var frames = new List<byte[]>();
        foreach (var projection in projections)
        {
            ArgumentNullException.ThrowIfNull(projection);
            frames.AddRange(projection switch
            {
                SelectCharacterDarkKnightComboProjection value =>
                    SelectCharacterSupplementalProjectionCodec.EncodeDarkKnightCombo(value),
                SelectCharacterVisibilityProjection value =>
                    [SelectCharacterSupplementalProjectionCodec.EncodeVisibility(value)],
                SelectCharacterCloneTitleProjection value =>
                    [SelectCharacterSupplementalProjectionCodec.EncodeCloneTitle(value)],
                SelectCharacterRosterProjection value =>
                    [SelectCharacterSupplementalProjectionCodec.EncodeRoster(value)],
                SelectCharacterHonorProjection value =>
                    [SelectCharacterSupplementalProjectionCodec.EncodeHonor(value)],
                SelectCharacterGrowthCapsuleProjection value =>
                    SelectCharacterSupplementalProjectionCodec.EncodeGrowthCapsule(value),
                SelectCharacterInventoryUpdateProjection value =>
                    SelectCharacterSupplementalProjectionCodec.EncodeInventoryUpdates([value]),
                SelectCharacterAdmissionRejectionProjection value =>
                    SelectCharacterSupplementalProjectionCodec.EncodeRejection(value),
                SelectCharacterPvpLobbyProjection value =>
                    SelectCharacterSupplementalProjectionCodec.EncodePvpLobby(value).DirectFrames,
                SelectCharacterPvpIdentityProjection value =>
                    [SelectCharacterSupplementalProjectionCodec.EncodePvpIdentity(value)],
                SelectCharacterTrailingEquipmentLocksProjection value =>
                    [SelectCharacterSupplementalProjectionCodec.EncodeTrailingEquipmentLocks(value.Result)],
                SelectCharacterAcceptedQuestsProjection value =>
                    SelectCharacterSupplementalProjectionCodec.EncodeAcceptedQuests(value.Quests),
                SelectCharacterPetRevivalProjection value =>
                    SelectCharacterSupplementalProjectionCodec.EncodePetRevival(value),
                SelectCharacterDungeonRejoinProjection value =>
                    SelectCharacterSupplementalProjectionCodec.EncodeDungeonRejoin(value),
                SelectCharacterExpertStoreCloseProjection value =>
                    [SelectCharacterSupplementalProjectionCodec.EncodeExpertStoreClose(value.OwnerUserId)],
                SelectCharacterTownLeaveProjection value =>
                    [SelectCharacterSupplementalProjectionCodec.EncodeTownLeave(value.UserId)],
                _ => throw new InvalidOperationException(
                    $"Supplemental projection '{projection.GetType().Name}' has no Networking codec."),
            });
        }
        return frames;
    }
}
