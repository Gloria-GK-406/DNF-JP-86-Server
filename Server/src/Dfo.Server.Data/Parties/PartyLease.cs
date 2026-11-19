using System.Collections.Immutable;

namespace Dfo.Server.Data.Parties;

/// <summary>Exclusive credential for a set of party containers. Keys are ordered by squad number.</summary>
public sealed record PartyLease(Guid Owner, ImmutableArray<PartyKey> Keys);

/// <summary>One numbered squad. The first member, when present, leads the squad.</summary>
public sealed record PartyLeaseSquad(int Number, IReadOnlyList<PartyCharacterSnapshot> Members);

public sealed record PartyLeaseMutation(IReadOnlyList<PartyNotificationPlan> Notifications);
