using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

/// <summary>The claim's lifetime: who holds it after each way a settlement can begin or end.</summary>
[Trait("Category", "Unit")]
public sealed class DungeonClearSettlementDomainServiceTests
{
    private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(10), new NetworkConnectionId("clear"), 1, 10011);

    // Nothing these paths reach reads the run beyond its identity.
    private static readonly DungeonRunState Run =
        (DungeonRunState)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DungeonRunState));

    [Fact]
    public async Task A_run_that_has_not_met_its_rule_is_never_claimed()
    {
        var f = new Fixture { CanComplete = false };
        var start = await f.Service.BeginAsync(Session, Run, default);
        Assert.Equal(ClearSettlementStartKind.NotReady, start.Kind);
        Assert.Equal(0, f.Claims);
    }

    [Fact]
    public async Task A_session_out_of_the_world_is_never_claimed()
    {
        var f = new Fixture { InWorld = false };
        var start = await f.Service.BeginAsync(Session, Run, default);
        Assert.Equal(ClearSettlementStartKind.NotInWorld, start.Kind);
        Assert.Equal(0, f.Claims);
    }

    [Fact]
    public async Task A_settlement_already_claimed_is_reported_as_such()
    {
        var f = new Fixture { ClaimAvailable = false };
        var start = await f.Service.BeginAsync(Session, Run, default);
        Assert.Equal(ClearSettlementStartKind.AlreadySettled, start.Kind);
        Assert.Equal(0, f.Releases);
    }

    [Fact]
    public async Task A_character_with_no_progress_releases_the_claim()
    {
        var f = new Fixture();
        var start = await f.Service.BeginAsync(Session, Run, default);
        Assert.Equal(ClearSettlementStartKind.Abandoned, start.Kind);
        Assert.Null(start.Attempt);
        Assert.Equal(1, f.Releases);
    }

    [Fact]
    public async Task A_failure_while_fixing_releases_the_claim_and_propagates()
    {
        var failure = new IOException("progress");
        var f = new Fixture { Failure = failure };
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => f.Service.BeginAsync(Session, Run, default).AsTask()));
        Assert.Equal(1, f.Releases);
    }

    [Fact]
    public async Task An_uncertain_outcome_keeps_the_claim()
    {
        var f = new Fixture
        {
            Failure = new TransactionOutcomeUncertainException(
                TransactionUncertaintyReason.PostCommitPublicationFailure, "unknown", new IOException("cause")),
        };
        await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => f.Service.BeginAsync(Session, Run, default).AsTask());
        Assert.Equal(0, f.Releases);
    }

    [Fact]
    public void Finishing_a_claim_that_is_no_longer_current_throws()
    {
        var f = new Fixture();
        Assert.Throws<InvalidOperationException>(() => f.Service.Finish(f.Attempt()));
    }

    [Fact]
    public void Abandoning_releases_exactly_the_attempts_claim()
    {
        var f = new Fixture();
        f.Service.Abandon(f.Attempt());
        Assert.Equal(1, f.Releases);
    }

    private sealed class Fixture
    {
        public bool CanComplete { get; init; } = true;
        public bool InWorld { get; init; } = true;
        public bool ClaimAvailable { get; init; } = true;
        public Exception? Failure { get; init; }
        public int Claims { get; private set; }
        public int Releases { get; private set; }

        public DungeonClearSettlementDomainService Service =>
            ProtocolAlgorithmFixture.Create<DungeonClearSettlementDomainService>(
                Stub<IDungeonRunSettlementDomainService>.Of(Runs),
                Stub<ISelectedSessionDomainService>.Of((method, _) => method.Name == nameof(ISelectedSessionDomainService.IsInWorld) ? InWorld : throw new NotSupportedException(method.Name)),
                Stub<ICharacterDomainService>.Of((method, _) => method.Name == nameof(ICharacterDomainService.Get)
                    ? Stub<ICharacter>.Of((inner, _) => inner.Name == nameof(ICharacter.ProgressAsync)
                        ? Progress()
                        : throw new NotSupportedException(inner.Name))
                    : throw new NotSupportedException(method.Name)));

        private ValueTask<CharacterProgressionState?> Progress() =>
            Failure is { } failure ? ValueTask.FromException<CharacterProgressionState?>(failure) : ValueTask.FromResult<CharacterProgressionState?>(null);

        private object? Runs(MethodInfo method, object?[] args)
        {
            switch (method.Name)
            {
                case nameof(IDungeonRunSettlementDomainService.SettlementRulesForAsync):
                    return ValueTask.FromResult(new DungeonSettlementRules(CanComplete, false));
                case nameof(IDungeonRunSettlementDomainService.TryClaimSettlement):
                    Claims++;
                    return ClaimAvailable ? Claim() : null;
                case nameof(IDungeonRunSettlementDomainService.ReleaseSettlement):
                    Releases++;
                    return true;
                case nameof(IDungeonRunSettlementDomainService.CommitSettlement):
                    return null;
                default:
                    throw new NotSupportedException(method.Name);
            }
        }

        public ClearSettlementAttempt Attempt() => new(Claim(), 1, null!, null, null);

        private static DungeonSettlementClaim Claim() => new(Session.CharacterId, null!, 1);
    }

    /// <summary>An interface answered by one delegate.</summary>
    public class Stub<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[], object?> answer = null!;

        internal static T Of(Func<MethodInfo, object?[], object?> answer)
        {
            var proxy = Create<T, Stub<T>>();
            ((Stub<T>)(object)proxy).answer = answer;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) => answer(method!, args ?? []);
    }
}
