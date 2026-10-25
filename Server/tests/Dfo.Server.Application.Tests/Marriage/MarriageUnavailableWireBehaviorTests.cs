using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class MarriageUnavailableWireBehaviorTests
{
    [Fact]
    public async Task SameTown_normal_unavailable_commits_expiry_and_does_not_dispatch_a_second_rejection()
    {
        await using var w = await ChurchTownAsync(Tier0(), s =>
        {
            if (!s.Any(x => x.ServiceType == typeof(MessageHandlerRegistration)))
            {
                var legacy = s.Where(x => x.ImplementationInstance is CommandHandlerRegistration command
                    && command.CommandType.Name is "MarriageProposalCommand" or "MarriageCeremonyCommand").ToArray();
                foreach (var registration in legacy) s.Remove(registration);
                new ProtocolModule().ConfigureServices(s, new ConfigurationBuilder().Build());
            }
            RecordedOutbound.AddTo(s);
            s.AddSingleton<TraceCouple>(sp => new(sp.GetRequiredService<MarriageDomainService>()));
            s.AddSingleton<IMarriageCoupleDomainService>(sp => sp.GetRequiredService<TraceCouple>());
        });
        var f = w.F;
        await EligibleAsync(w, f.Session, w.Peer);
        Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(f.Session, w.Peer, 0, 0, default))).Succeeded);
        MoveTo(f, w.Peer, 2, 2);
        var users = f.Services.GetRequiredService<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>();
        Assert.True(users.TryGetByConnection(w.Peer.ConnectionId, out var user)); Assert.NotNull(user);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(user.Connection),
            new WeddingResponseRequest(GameMessageMetadata.Empty, f.Session.WireUserId, 2, 0, 0, 0), default);
        var call = Assert.Single(f.Services.GetRequiredService<TraceCouple>().Replies);
        Assert.Equal((byte)0, call.Response); Assert.Equal(MarriageCeremonyFailure.Unavailable, call.Result.Failure);
        Assert.Equal(MarriageCeremonyState.Expired, call.Result.Ceremony!.State);
        Assert.Equal((int)MarriageCeremonyState.Expired, f.Scalar("SELECT state FROM marriage_ceremonies"));
        Assert.Equal((int)MarriageStage.Engaged, f.Scalar("SELECT stage FROM marriage_relations"));
        Assert.Equal(500000, f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71"));
    }
    private sealed class TraceCouple(IMarriageCoupleDomainService inner) : IMarriageCoupleDomainService
    {
        internal List<(byte Response, MarriageCeremonyResult Result)> Replies { get; } = [];
        public ValueTask<MarriageProposalResult> ProposeAsync(SelectedSessionIdentity a, SelectedSessionIdentity b, CancellationToken ct) => inner.ProposeAsync(a, b, ct);
        public ValueTask<MarriageProposalResult> RespondProposalAsync(SelectedSessionIdentity a, SelectedSessionIdentity b, byte value, CancellationToken ct) => inner.RespondProposalAsync(a, b, value, ct);
        public ValueTask<MarriageCeremonyResult> RequestReservationAsync(SelectedSessionIdentity a, SelectedSessionIdentity b, int tier, int wait, CancellationToken ct) => inner.RequestReservationAsync(a, b, tier, wait, ct);
        public async ValueTask<MarriageCeremonyResult> RespondReservationAsync(SelectedSessionIdentity a, SelectedSessionIdentity b, byte value, CancellationToken ct)
        {
            var result = await inner.RespondReservationAsync(a, b, value, ct);
            Replies.Add((value, result)); return result;
        }
        public ValueTask<IReadOnlyList<MarriageWeddingAdmission>> OpenWeddingAsync(MarriageCeremony ceremony, CancellationToken ct) => inner.OpenWeddingAsync(ceremony, ct);
        public ValueTask<MarriageLovePointResult> UseLovePointAsync(SelectedSessionIdentity actor, BusinessSlot slot, IReadOnlyList<AccountId> accounts, IEffectFootprintRecorder footprint, CancellationToken ct) => inner.UseLovePointAsync(actor, slot, accounts, footprint, ct);
    }
}
