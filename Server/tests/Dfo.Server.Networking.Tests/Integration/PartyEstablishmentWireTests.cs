using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Testing;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Integration;

[Collection(CharacterizationCollection.Name)]
public sealed class PartyEstablishmentWireTests
{
    [PvfFact]
    public async Task Peer_types_outside_party_do_not_create_an_invitation_and_realtime_is_requester_only()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        var ct = timeout.Token;
        await using var server = await CharacterizationHost.StartAsync(null, ct);
        var (leader, member) = await TwoPlayersInTownAsync(server, ct);
        using var first = leader;
        using var second = member;
        var leaderStart = leader.Frames.Count();
        var memberStart = member.Frames.Count();
        await leader.SendAsync(0x000A, [.. U16(member.WireUserId), 1, 64, 0, 0, 0], ct);
        await member.SendAsync(0x000B, [.. U16(leader.WireUserId), 2, 0, 0, 0, 0], ct);
        await FenceAllAsync(ct, leader, member);
        Assert.DoesNotContain(leader.Frames.Skip(leaderStart), IsParty);
        Assert.DoesNotContain(member.Frames.Skip(memberStart), IsParty);

        await FormPartyAsync(leader, member, ct);
        leaderStart = leader.Frames.Count();
        memberStart = member.Frames.Count();
        await member.SendAsync(0x00A6, [], ct);
        await FenceAllAsync(ct, member, leader);
        Assert.DoesNotContain(leader.Frames.Skip(leaderStart), IsParty);
        var answer = Assert.Single(member.Frames.Skip(memberStart), IsParty);
        Assert.True(answer.Is(0, 0x0099));
        Assert.False(leader.IsClosed);
        Assert.False(member.IsClosed);
    }

    [PvfFact]
    public async Task Actual_udp_decoder_dispatcher_observes_authenticated_session_before_selection()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        var ct = timeout.Token;
        EndpointProbe? probe = null;
        await using var server = await CharacterizationHost.StartAsync(new CharacterizationOptions
        {
            Decorate = services => services.Decorate<IGameSessionEndpointDomainService>((provider, inner) =>
                probe = new EndpointProbe(inner, provider.GetRequiredService<IOnlineUserQueryDomainService>())),
        }, ct);
        var (mid, ticket) = await server.RegisterAndIssueTicketAsync("udp-before-select", ct);
        using var client = await server.ConnectAsync("udp", ct);
        await client.LoginAsync(mid, ticket, ct);
        var start = client.Frames.Count();
        await client.SendAsync(0x0002, [5, 192, 0, 2, 1, 198, 51, 100, 1, 0x79, 0x00, 0xC0, 0x05, 0, 0], ct);
        await client.FenceAsync(ct);
        var observed = Assert.Single(probe!.Observations);
        Assert.Null(observed.Owner.CurrentCharacterId);
        Assert.NotNull(observed.Owner.AccountId);
        Assert.True(observed.Accepted);
        Assert.Equal(new SessionIpv4Address(127, 0, 0, 1), observed.Endpoint.InnerAddress);
        Assert.Equal(observed.Endpoint.InnerAddress, observed.Endpoint.OuterAddress);
        Assert.Equal((ushort)30976, observed.Endpoint.Port);
        Assert.Equal(1500U, observed.Endpoint.Mtu);
        Assert.Equal(0, observed.Endpoint.NatType);
        Assert.Equal(observed.Endpoint, probe.Find(observed.Owner.SessionIdentity));
        Assert.All(client.Frames.Skip(start), frame => Assert.True(frame.Is(1, WireClient.FenceType)));
        Assert.False(client.IsClosed);
    }

    private sealed class EndpointProbe(IGameSessionEndpointDomainService inner,
        IOnlineUserQueryDomainService users) : IGameSessionEndpointDomainService
    {
        internal List<(OnlineUserSnapshot Owner, SessionEndpoint Endpoint, bool Accepted)> Observations { get; } = [];
        public bool TryObserve(GameSessionIdentity session, SessionEndpoint endpoint)
        {
            var owner = users.GetRequired(session.OnlineUserId);
            var accepted = inner.TryObserve(session, endpoint);
            Observations.Add((owner, endpoint, accepted));
            return accepted;
        }
        public SessionEndpoint? Find(GameSessionIdentity session) => inner.Find(session);
    }

    [PvfTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Actual_encoder_failure_retries_only_the_failed_exact_target_and_keeps_stage_watermark(int failures)
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        var ct = timeout.Token;
        PartyEncoderProbe? probe = null;
        var options = new CharacterizationOptions
        {
            Decorate = services =>
            {
                var original = services.Last(value => value.ServiceType == typeof(IOutboundMessageEncoder));
                services.Remove(original);
                services.AddSingleton<IOutboundMessageEncoder>(provider =>
                {
                    var inner = original.ImplementationInstance as IOutboundMessageEncoder
                        ?? original.ImplementationFactory?.Invoke(provider) as IOutboundMessageEncoder
                        ?? (IOutboundMessageEncoder)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
                    return probe = new PartyEncoderProbe(inner,
                        provider.GetRequiredService<IPartyProjectionStateDomainService>(), failures);
                });
            },
        };
        await using var server = await CharacterizationHost.StartAsync(options, ct);
        var (leader, member) = await TwoPlayersInTownAsync(server, ct);
        using var first = leader;
        using var second = member;
        await leader.SendAsync(0x000A, [.. U16(member.WireUserId), 0, 64, 0, 0, 0], ct);
        await FenceAllAsync(ct, leader, member);
        Assert.Contains(member.Frames, frame => frame.Is(0, 7));
        probe!.Enabled = true;
        var leaderStart = leader.Frames.Count();
        var memberStart = member.Frames.Count();
        await member.SendAsync(0x000B, [.. U16(leader.WireUserId), 0, 0, 0, 0, 0], ct);
        await FenceAllAsync(ct, member, leader);
        var produced = leader.Frames.Skip(leaderStart).Concat(member.Frames.Skip(memberStart)).ToArray();
        Assert.Equal(failures == 1 ? 3 : 2, probe.Attempts[probe.Target!.Value]);
        Assert.Equal(failures == 1 ? 2 : 1, Assert.Single(probe.Attempts, value => value.Key != probe.Target).Value);
        Assert.Equal(failures, probe.Thrown);
        Assert.Equal(failures == 1 ? 2 : 0, produced.Count(frame => frame.Is(0, 11)));
        Assert.Equal(failures == 1 ? 1 : 0, produced.Count(frame => frame.Is(0, 8)));
        TestGameplay.Run(() => Assert.Equal(failures == 1 ? probe.Snapshot!.Revision : 0,
            probe.State.StreamOf(probe.Snapshot!.Generation).LatestRevision));
        Assert.False(leader.IsClosed);
        Assert.False(member.IsClosed);
    }

    private sealed class PartyEncoderProbe(IOutboundMessageEncoder inner,
        IPartyProjectionStateDomainService state, int failures) : IOutboundMessageEncoder
    {
        internal bool Enabled { get; set; }
        internal NetworkConnectionId? Target { get; private set; }
        internal PartySnapshot? Snapshot { get; private set; }
        internal IPartyProjectionStateDomainService State { get; } = state;
        internal Dictionary<NetworkConnectionId, int> Attempts { get; } = [];
        internal int Thrown { get; private set; }
        private void Observe(NetworkConnectionContext connection, IEnumerable<IServerNetworkMessage> messages)
        {
            if (!Enabled || messages.OfType<PartyRealtimeMessage>().FirstOrDefault() is not { } realtime) return;
            Snapshot = realtime.Snapshot;
            Target ??= connection.ConnectionId;
            Attempts[connection.ConnectionId] = Attempts.GetValueOrDefault(connection.ConnectionId) + 1;
            if (connection.ConnectionId == Target && Thrown < failures)
            {
                Thrown++;
                throw new IOException("TASK38 actual party encoder failure");
            }
        }
        public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message)
        {
            var bytes = inner.EncodeMessage(connection, message);
            Observe(connection, [message]);
            return bytes;
        }
        public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages)
        {
            var bytes = inner.EncodeBatch(connection, messages);
            Observe(connection, messages);
            return bytes;
        }
    }

    private static bool IsParty(WireFrame frame) =>
        frame.Is(0, 7) || frame.Is(0, 8) || frame.Is(0, 9) || frame.Is(0, 11) || frame.Is(0, 0x0099);
}
