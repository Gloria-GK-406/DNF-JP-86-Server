using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

/// <summary>Bounded operation identities belong to the original physical connection, never its reusable ID.</summary>
internal sealed class EffectOperationLedger(SelectedSessionSafety safety, ISelectedSessionDomainService sessions)
{
    private const long Window = 128;
    private readonly ConditionalWeakTable<NetworkConnectionContext, Ledger> ledgers = new();
    internal EffectAdmission Admit(NetworkConnectionContext connection, IEffectSettlementRequest request)
    {
        GameplayWriteAssertion.Writing(nameof(EffectOperationLedger) + "." + nameof(Admit));
        var metadata = request.Metadata;
        if (metadata.InboundOrdinal is not > 0 || metadata.FullBodyLength is not (>= 0 and <= 1_048_576)
            || metadata.FullBodySha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Effect requests require transport ordinal and full raw-body identity.");
        var session = sessions.FindByConnection(connection.ConnectionId);
        if (session is null)
        {
            return new EffectAdmission.Rejected();
        }
        var recipient = safety.Capture(session, metadata);
        if (!ReferenceEquals(recipient.Connection, connection))
            throw new InvalidOperationException("Effect request is not from the captured physical recipient.");
        // Capture before registering: an old request must never bind to a replacement character.
        var ledger = ledgers.GetValue(connection, _ => new());
        safety.DemandCurrent(recipient);
        var ordinal = metadata.InboundOrdinal.Value;
        if (ordinal <= ledger.LatestOrdinal) throw new InvalidDataException("Transport ordinal must advance.");
        ledger.LatestOrdinal = ordinal;
        foreach (var key in ledger.Entries.Where(x => ordinal - x.Value.Ordinal >= Window).Select(x => x.Key).ToArray())
            ledger.Entries.Remove(key);
        var identity = (session, metadata.Sequence);
        EffectOperationEntry entry;
        if (metadata.SequenceIsFresh)
        {
            entry = new(session, ordinal, metadata.Selector, metadata.Type, metadata.Sequence, hash, metadata.FullBodyLength.Value);
            ledger.Entries[identity] = entry;
        }
        else
        {
            if (!ledger.Entries.TryGetValue(identity, out entry!) || entry.Selector != metadata.Selector
                || entry.Type != metadata.Type || entry.Hash != hash || entry.Length != metadata.FullBodyLength.Value)
                throw new InvalidDataException("Unknown, expired or conflicting effect request identity.");
            if (entry.State == EffectOperationState.Committed)
            {
                return new EffectAdmission.Replay(entry, recipient);
            }
            if (entry.State != EffectOperationState.ConfirmedRollback)
                throw new InvalidDataException($"Effect request cannot retry a {entry.State} operation.");
            entry.State = EffectOperationState.Pending;
            entry.Projection = null;
        }
        return new EffectAdmission.Selected(entry, recipient, request.MinimumReplayNeeds);
    }
    internal static void FinalizePending(EffectOperationEntry entry)
    {
        GameplayWriteAssertion.Writing(nameof(EffectOperationLedger) + "." + nameof(FinalizePending));
        if (entry.State == EffectOperationState.Pending) entry.State = EffectOperationState.Rejected;
    }

    private sealed class Ledger
    {
        public long LatestOrdinal { get; set; }
        public Dictionary<(SelectedSessionIdentity, ushort), EffectOperationEntry> Entries { get; } = [];
    }
}

internal abstract record EffectAdmission
{
    private EffectAdmission() { }
    internal sealed record Rejected : EffectAdmission;
    internal sealed record Selected(EffectOperationEntry Entry, SelectedSessionRecipient Recipient, ReplayProjectionNeeds Minimum) : EffectAdmission;
    internal sealed record Replay(EffectOperationEntry Entry, SelectedSessionRecipient Recipient) : EffectAdmission;
}
