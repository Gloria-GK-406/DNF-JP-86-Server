using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// AC-011's wire-level monster-death regression (TASK-24 section 8), on the characterization host reused read-only:
/// real TouchSocket, real handlers and commands, real registration adapters, outbox and outbound sender. The fault is
/// injected at the registration point's encoder (a non-transport failure), never in the transport. The selection
/// case went to the sender level (ruling 3): SelectCharacterLifecycleTests.
/// </summary>
[Collection(CharacterizationCollection.Name)]
public sealed class PostCommitFailureWireTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>
    /// After a party kill commits, the reporter's own death batch cannot be registered. Every participant is fenced
    /// (RQ-023(c)) and every participant's connection is closed; neither is told of the kill.
    /// </summary>
    [PvfFact]
    public async Task A_monster_death_failing_after_its_commit_fences_and_closes_every_participant()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        var fault = new DeathEncodingFault();
        var fences = new ConcurrentQueue<SelectedSessionIdentity>();
        IDungeonRunDomainService? runs = null;
        await using var server = await CharacterizationHost.StartAsync(new CharacterizationOptions
        {
            Decorate = services =>
            {
                ReplaceEncoder(services, inner => new FaultingEncoder(inner, fault.ThrowIfArmed));
                services.Decorate<ISelectedSessionDomainService>((_, inner) =>
                    Recording<ISelectedSessionDomainService>.Wrap(inner, (method, args) =>
                    {
                        if (method.Name == nameof(ISelectedSessionDomainService.Fence))
                            fences.Enqueue((SelectedSessionIdentity)args![0]!);
                    }));
                // Not a decorator: the run service itself, kept so the test can read which room has a monster.
                services.Decorate<IDungeonRunQueryDomainService>((provider, inner) => { runs = provider.DungeonRuns(); return inner; });
            },
        }, timeout.Token);
        var (leader, member) = await TwoPlayersInTownAsync(server, timeout.Token);
        using var _ = leader;
        using var __ = member;
        await FormPartyAsync(leader, member, timeout.Token);
        await OpenDungeonListAsync(leader, member, timeout.Token);
        await EnterDungeonAsync(leader, member, 144, frame => frame.Is(0x00, 0x001D), timeout.Token);
        foreach (var client in new[] { leader, member })
        {
            await client.SendAsync(0x0025, new byte[8], timeout.Token);
            await FenceAllAsync(timeout.Token, client);
        }

        // The start room of 144 is empty: the party walks into a neighbouring room that holds a live monster.
        var actor = await MoveToARoomWithAMonsterAsync(runs!, leader, member, timeout.Token);
        output.WriteLine($"killing actor {actor}");

        fault.Arm();
        StepAll("leader reports the kill", leader, member);
        await leader.SendAsync(0x0027, DeathBody(actor, leader.WireUserId), timeout.Token);

        await leader.ReadUntilClosedAsync(timeout.Token);
        await member.ReadUntilClosedAsync(timeout.Token);
        Assert.True(fault.Fired, "The kill did not commit.");
        Assert.True(leader.IsClosed);
        Assert.True(member.IsClosed);
        Assert.DoesNotContain(member.Frames, frame => frame.Is(0x00, 0x0026));
        Assert.Equal(2, fences.Select(session => session.CharacterId).Distinct().Count());
    }

    private async Task<ushort> MoveToARoomWithAMonsterAsync(
        IDungeonRunDomainService runs, WireClient leader, WireClient member, CancellationToken cancellationToken)
    {
        var run = new[] { new CharacterId(1), new CharacterId(2) }.Select(runs.Find).OfType<DungeonRunState>().First();
        var start = run.CurrentCell;
        var neighbours = run.Layout.Cells.Keys
            .Where(cell => Math.Abs(cell.X - start.X) + Math.Abs(cell.Y - start.Y) == 1)
            .ToArray();
        foreach (var cell in neighbours)
        {
            StepAll($"leader moves to {cell.X},{cell.Y}", leader, member);
            await leader.SendAsync(0x002D, [cell.X, cell.Y, 0, 0, 0, 0, 0, 0, 0, 0], cancellationToken);
            await FenceAllAsync(cancellationToken, leader, member);
            var moved = runs.Find(run.Participants!.First().CharacterId)!;
            output.WriteLine($"cell {cell.X},{cell.Y}: now at {moved.CurrentCell.X},{moved.CurrentCell.Y}, "
                + $"{moved.CurrentCellState.Actors.Count(value => value.IsMonster && !value.IsDead)} live monsters");
            if (moved.CurrentCell == cell
                && moved.CurrentCellState.Actors.FirstOrDefault(value => value.IsMonster && !value.IsDead) is { } monster)
                return monster.Sequence;
        }

        throw new InvalidOperationException("No neighbouring room of the start room holds a live monster.");
    }

    private static byte[] DeathBody(ushort actor, ushort killer)
    {
        var body = new byte[62];
        BinaryPrimitives.WriteUInt32LittleEndian(body, actor);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), killer);
        return body;
    }

    /// <summary>Replaces the registration point's encoder; outside the domain-service decorators on purpose (section 8).</summary>
    private static void ReplaceEncoder(IServiceCollection services, Func<IOutboundMessageEncoder, IOutboundMessageEncoder> wrap)
    {
        var descriptor = services.Last(service => service.ServiceType == typeof(IOutboundMessageEncoder));
        services.Remove(descriptor);
        services.AddSingleton<IOutboundMessageEncoder>(provider => wrap(
            descriptor.ImplementationInstance as IOutboundMessageEncoder
                ?? (descriptor.ImplementationFactory is not null
                    ? (IOutboundMessageEncoder)descriptor.ImplementationFactory(provider)
                    : (IOutboundMessageEncoder)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))));
    }

    /// <summary>Refuses the first committed death notification (its owner is the reporter, an empty ack has none).</summary>
    private sealed class DeathEncodingFault
    {
        private int armed;

        public bool Fired { get; private set; }

        public void Arm() => Volatile.Write(ref armed, 1);

        public void ThrowIfArmed(IServerNetworkMessage message)
        {
            if (message.GetType().Name != "MonsterDeathNotification"
                || message.GetType().GetProperty("Owner")?.GetValue(message) is not ushort owner || owner == 0
                || Interlocked.Exchange(ref armed, 0) != 1)
                return;
            Fired = true;
            throw new InvalidDataException("Injected death encoding failure.");
        }
    }

    private sealed class FaultingEncoder(IOutboundMessageEncoder inner, Action<IServerNetworkMessage> check) : IOutboundMessageEncoder
    {
        public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message)
        {
            check(message);
            return inner.EncodeMessage(connection, message);
        }

        public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages)
        {
            foreach (var message in messages) check(message);
            return inner.EncodeBatch(connection, messages);
        }
    }

    /// <summary>A pass-through decorator that reports each call after it returns.</summary>
    public class Recording<T> : DispatchProxy where T : class
    {
        private T inner = null!;
        private Action<MethodInfo, object?[]?> called = null!;

        public static T Wrap(T inner, Action<MethodInfo, object?[]?> called)
        {
            var proxy = Create<T, Recording<T>>();
            var recording = (Recording<T>)(object)proxy;
            recording.inner = inner;
            recording.called = called;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                var result = targetMethod!.Invoke(inner, args);
                called(targetMethod, args);
                return result;
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }
}
