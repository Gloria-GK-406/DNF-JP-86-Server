using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Marriage;

public enum MarriageWeddingPhase { Preparing, Completed, Cancelled }
public sealed record MarriageWedding(MarriageCeremony Ceremony, MarriageWeddingPhase Phase, int PaidGold);
public sealed record MarriageWeddingAdmission(SelectedSessionIdentity Actor, MarriageChurchSnapshot Church);
public sealed record MarriageWeddingCleanup(CharacterId Character, MarriageChurchSnapshot Church);
public sealed record MarriageWeddingTransition(MarriageWedding Wedding, IReadOnlyList<MarriageWeddingAdmission> Participants,
    IReadOnlyList<MarriageWeddingAdmission> Guests)
{
    public IReadOnlyList<MarriageWeddingAdmission> Cleanup { get; init; } = [];
    public IReadOnlyList<MarriageWeddingDeparture> Departures { get; init; } = [];
}
public sealed record MarriageWeddingInvitation(Guid CeremonyId, CharacterId Guest, CharacterId Inviter, uint Key, int Gold = 0);
public sealed record MarriageWeddingLetter(SelectedSessionIdentity Guest, MarriageChurchSnapshot Church, uint Key);
public sealed record MarriageWeddingLetterResult(int Status, ushort Remaining, IReadOnlyList<MarriageWeddingLetter> Letters)
{
    public IReadOnlyList<uint> Invited { get; init; } = [];
}
public sealed class MarriageWeddingUnavailableException : InvalidOperationException
{
    public MarriageWeddingUnavailableException() : base("Wedding preparation could not be committed.") { }
}

public sealed record MarriageWeddingRules(int Gold, int InvitationLimit, int GiftItemId, int GiftMinimum, int GiftMaximum, int GiftChargePercent)
{
    public int InvitationCutoffSeconds { get; init; } = 60;
    public IReadOnlyList<MarriageGuestReward> GuestRewards { get; init; } = [];
    // PVF Underfoot NPC20013 and native departure builder RVA0089570B (+30 Y).
    public MarriageChurchLocation ExitLocation { get; init; } = new(2, 2, 2016, 212);
}

public sealed record MarriageWeddingAdvanceResult(MarriageWeddingTransition? Transition);

public sealed record MarriageWeddingDeparture(SelectedSessionIdentity Actor, int Channel, MarriageChurchLocation Location);
