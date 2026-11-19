using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Raids;

internal sealed class AntonRaidRewardSettlementDomainService(
    IAntonRaidRewardContentSource content, IDungeonDeadlineWake deadlines) : IAntonRaidRewardSettlementDomainService
{
    private static readonly TimeSpan PromptDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MovieLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SelectionLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SquadDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DeliveryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DeliveryRetryDelay = TimeSpan.FromSeconds(2);
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, Round> active = [];

    public AntonRaidRewardSnapshot? Begin(AntonRaidRewardSettlementSeed seed, DateTimeOffset now)
    {
        Validate(seed);
        lock (gate)
        {
            if (active.TryGetValue(seed.Instance, out var previous)
                && (previous.Seed.Phase >= seed.Phase || previous.Stage != AntonRaidRewardStage.Complete))
                return null;

            var definition = content.Read();
            var round = new Round(seed with { Eligible = seed.Eligible.ToArray(), Audience = seed.Audience.ToArray() },
                definition.Phases[seed.Phase], definition.RetryCertificate, now);
            active[seed.Instance] = round;
            Wake();
            return round.Snapshot();
        }
    }

    public AntonRaidRewardSnapshot? Find(Guid instance, byte phase)
    {
        lock (gate)
            return FindRound(instance, phase)?.Snapshot();
    }

    public AntonRaidMovieResult? FinishMovie(Guid instance, byte phase, CharacterId character, byte flag,
        DateTimeOffset now)
    {
        lock (gate)
        {
            var round = FindRound(instance, phase);
            if (round is null || flag > 1 || !round.Seed.Succeeded
                || (!round.Seed.Audience.Contains(character) && !round.Participants.ContainsKey(character))
                || round.Stage == AntonRaidRewardStage.Complete)
                return null;

            var opened = flag == 1 && round.Stage == AntonRaidRewardStage.Result;
            if (opened)
            {
                OpenCards(round, now);
                Wake();
            }
            return new(round.Snapshot(), opened);
        }
    }

    public AntonRaidRewardSelection Select(Guid instance, byte phase, CharacterId character, byte type, byte slot,
        DateTimeOffset now)
    {
        lock (gate)
        {
            var round = FindRound(instance, phase);
            if (round is null || slot > 3 || type > 2 || !round.Seed.Succeeded
                || round.Stage is AntonRaidRewardStage.Result or AntonRaidRewardStage.Complete
                || !round.Participants.TryGetValue(character, out var participant))
                return new(false);

            if (type == 2)
                return new(true);

            var kind = (AntonRaidRewardKind)type;
            if (participant.Cards.TryGetValue(kind, out var old))
                return new(true, round.Snapshot(), Grant: round.Grant(old));
            if (round.Stage != AntonRaidRewardStage.Cards)
                return new(false);

            var card = round.Reveal(participant, kind, slot);
            if (round.AllSelected)
            {
                round.Stage = AntonRaidRewardStage.SquadDelay;
                round.Deadline = now + SquadDelay;
                Wake();
            }
            return new(true, round.Snapshot(), card, round.Grant(card));
        }
    }

    public IReadOnlyList<AntonRaidRewardDeadline> Deadlines()
    {
        lock (gate)
            return active.Values.Where(round => round.Deadline.HasValue)
                .Select(round => new AntonRaidRewardDeadline(round.Seed.Instance, round.Seed.Phase, round.Deadline!.Value))
                .OrderBy(deadline => deadline.At).ToArray();
    }

    public AntonRaidRewardAdvance? Advance(AntonRaidRewardDeadline expected, DateTimeOffset now)
    {
        lock (gate)
        {
            var round = FindRound(expected.Instance, expected.Phase);
            if (round?.Deadline != expected.At || expected.At > now)
                return null;

            switch (round.Stage)
            {
                case AntonRaidRewardStage.Result when !round.Prompted:
                    round.Prompted = true;
                    round.Deadline = round.MovieFallback < now ? now : round.MovieFallback;
                    return new(AntonRaidRewardAdvanceKind.MoviePrompt, round.Snapshot(), [], []);

                case AntonRaidRewardStage.Result:
                    OpenCards(round, now);
                    return new(AntonRaidRewardAdvanceKind.CardsOpened, round.Snapshot(), [], []);

                case AntonRaidRewardStage.Cards:
                    var opened = round.RevealUnselected();
                    round.Stage = AntonRaidRewardStage.SquadDelay;
                    round.Deadline = now + SquadDelay;
                    return new(AntonRaidRewardAdvanceKind.CardsAutoSelected, round.Snapshot(),
                        opened, opened.Select(round.Grant).ToArray());

                case AntonRaidRewardStage.SquadDelay:
                    round.Stage = AntonRaidRewardStage.SquadItems;
                    round.Deadline = now + DeliveryDelay;
                    return new(AntonRaidRewardAdvanceKind.SquadItemsShown, round.Snapshot(), round.SquadCards, []);

                case AntonRaidRewardStage.SquadItems:
                case AntonRaidRewardStage.Delivering:
                    round.Stage = AntonRaidRewardStage.Delivering;
                    round.Deadline = now + DeliveryRetryDelay;
                    return new(AntonRaidRewardAdvanceKind.Deliver, round.Snapshot(), [], round.Grants());

                default:
                    return null;
            }
        }
    }

    public bool Complete(Guid instance, byte phase)
    {
        lock (gate)
        {
            var round = FindRound(instance, phase);
            if (round?.Stage != AntonRaidRewardStage.Delivering)
                return false;
            round.Stage = AntonRaidRewardStage.Complete;
            round.Deadline = null;
            return true;
        }
    }

    public void Forget(Guid instance)
    {
        lock (gate)
            active.Remove(instance);
    }

    private Round? FindRound(Guid instance, byte phase) =>
        active.TryGetValue(instance, out var round) && round.Seed.Phase == phase ? round : null;

    private void Wake() => deadlines.Wake(DungeonDeadlineKind.AntonRaidRewards);

    private static void OpenCards(Round round, DateTimeOffset now)
    {
        round.Stage = AntonRaidRewardStage.Cards;
        round.Deadline = now + SelectionLimit;
    }

    private static void Validate(AntonRaidRewardSettlementSeed seed)
    {
        if (seed.Instance == Guid.Empty || seed.Phase > 1 || seed.DeathCount < 0 || seed.EndedAt < seed.StartedAt
            || seed.Eligible.Count > 20 || seed.Audience.Count > 20
            || seed.Eligible.DistinctBy(member => member.Character).Count() != seed.Eligible.Count
            || seed.Eligible.Any(member => member.Role > 2 || member.Squad > 10)
            || seed.Eligible.Where(member => member.Squad != 0).GroupBy(member => member.Squad).Any(squad => squad.Count() > 4))
            throw new ArgumentException("Invalid Anton raid reward settlement.", nameof(seed));
    }

    private sealed class Round
    {
        private static readonly AntonRaidRewardKind[] SelectableKinds = [AntonRaidRewardKind.PartyCard, AntonRaidRewardKind.Gold];
        private readonly AntonRaidRewardItem retry;
        internal AntonRaidRewardSettlementSeed Seed { get; }
        internal Dictionary<CharacterId, Participant> Participants { get; } = [];
        internal byte Rank { get; }
        internal AntonRaidRewardStage Stage { get; set; }
        internal DateTimeOffset MovieFallback { get; }
        internal DateTimeOffset? Deadline { get; set; }
        internal bool Prompted { get; set; }
        internal IReadOnlyList<AntonRaidRewardCard> SquadCards { get; }
        internal bool AllSelected => Participants.Values.All(member => member.Cards.Count == SelectableKinds.Length);

        internal Round(AntonRaidRewardSettlementSeed seed, AntonRaidPhaseRewardDefinition phase,
            AntonRaidRewardItem retry, DateTimeOffset now)
        {
            Seed = seed;
            this.retry = retry;
            Rank = AntonRaidSettlementRules.Rank(phase, seed.DeathCount);
            MovieFallback = now + MovieLimit;

            var groups = new Dictionary<byte, Squad>();
            foreach (var recipient in seed.Eligible.OrderBy(member => member.Character.Value))
            {
                Squad? squad = null;
                if (seed.Succeeded)
                {
                    if (recipient.Squad == 0)
                        squad = new(phase, Rank);
                    else if (!groups.TryGetValue(recipient.Squad, out squad))
                    {
                        squad = new(phase, Rank);
                        groups.Add(recipient.Squad, squad);
                    }
                }
                Participants.Add(recipient.Character, new(recipient, squad));
            }

            if (seed.Succeeded && Participants.Count > 0)
            {
                Stage = AntonRaidRewardStage.Result;
                Deadline = now + PromptDelay;
                SquadCards = Participants.Values.Select(participant =>
                {
                    var (item, flags) = AntonRaidSettlementRules.Draw(phase, AntonRaidRewardKind.SquadItem, Rank);
                    return new AntonRaidRewardCard(participant.Recipient.Character,
                        AntonRaidRewardKind.SquadItem, 1, flags, item);
                }).ToArray();
            }
            else
            {
                Stage = AntonRaidRewardStage.Delivering;
                Deadline = now;
                SquadCards = [];
            }
        }

        internal AntonRaidRewardSnapshot Snapshot() => new(Seed, Rank, Stage,
            Participants.Values.SelectMany(member => member.Cards.Values).ToArray());

        internal AntonRaidRewardCard Reveal(Participant participant, AntonRaidRewardKind kind, byte requestedSlot)
        {
            var squad = participant.Squad!;
            if (participant.Slot is null)
            {
                var slot = squad.Occupied[requestedSlot] ? Array.IndexOf(squad.Occupied, false) : requestedSlot;
                if (slot < 0)
                    throw new InvalidOperationException("Anton raid squad has no free card.");
                squad.Occupied[slot] = true;
                participant.Slot = (byte)slot;
            }
            var chosen = participant.Slot.Value;
            var (item, flags) = squad.Cards[kind][chosen];
            var card = new AntonRaidRewardCard(participant.Recipient.Character, kind, chosen, flags, item);
            participant.Cards.Add(kind, card);
            return card;
        }

        internal AntonRaidRewardCard[] RevealUnselected()
        {
            var revealed = new List<AntonRaidRewardCard>();
            foreach (var participant in Participants.Values)
            foreach (var kind in SelectableKinds)
                if (!participant.Cards.ContainsKey(kind))
                    revealed.Add(Reveal(participant, kind, 0));
            return revealed.ToArray();
        }

        internal AntonRaidRewardGrant Grant(AntonRaidRewardCard card) =>
            Grant(Participants[card.Character].Recipient, card.Kind, card.Item);

        internal IReadOnlyList<AntonRaidRewardGrant> Grants() => Seed.Succeeded
            ? Participants.Values.SelectMany(member => member.Cards.Values).Select(Grant)
                .Concat(SquadCards.Select(Grant))
                .Concat(Participants.Values.Select(member => Grant(member.Recipient, AntonRaidRewardKind.Quest, null))).ToArray()
            : Participants.Values.Select(member => Grant(member.Recipient, AntonRaidRewardKind.RetryCertificate, retry)).ToArray();

        private AntonRaidRewardGrant Grant(AntonRaidRewardParticipant recipient, AntonRaidRewardKind kind, AntonRaidRewardItem? item) =>
            new(Seed.Instance, Seed.Phase, recipient, kind, item, Seed.EndedAt);
    }

    private sealed class Participant(AntonRaidRewardParticipant recipient, Squad? squad)
    {
        internal AntonRaidRewardParticipant Recipient { get; } = recipient;
        internal Squad? Squad { get; } = squad;
        internal byte? Slot { get; set; }
        internal Dictionary<AntonRaidRewardKind, AntonRaidRewardCard> Cards { get; } = [];
    }

    private sealed class Squad(AntonRaidPhaseRewardDefinition phase, byte rank)
    {
        internal bool[] Occupied { get; } = new bool[4];
        internal Dictionary<AntonRaidRewardKind, (AntonRaidRewardItem Item, byte Flags)[]> Cards { get; } =
            new[] { AntonRaidRewardKind.PartyCard, AntonRaidRewardKind.Gold }.ToDictionary(kind => kind,
                kind => Enumerable.Range(0, 4).Select(_ => AntonRaidSettlementRules.Draw(phase, kind, rank)).ToArray());
    }
}
