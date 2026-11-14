using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Pvp;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Pvp;

public sealed class SqlitePvpRecordDomainStoreTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);
    private static readonly CharacterId Winner = new(71);
    private static readonly CharacterId Loser = new(72);

    [Fact]
    public async Task A_new_record_has_no_experience_and_keeps_its_grades()
    {
        await WithStoreAsync(async (store, _, _) =>
        {
            var record = await store.ReadAsync(Winner, default);

            Assert.Equal(new PvpRecord(3, 4, null, 0, 0, 0, 0, 0), record);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ReadAsync(new(99), default));
        });
    }

    [Fact]
    public async Task The_first_settlement_creates_and_a_repeat_returns_the_same_results_without_writing()
    {
        await WithStoreAsync(async (store, scope, path) =>
        {
            var match = Guid.NewGuid();
            var first = await SettleAsync(scope, store, Decided(match));
            // Different numbers under the same identity: nothing of them may land.
            var again = await SettleAsync(scope, store, Decided(match) with
            {
                Fighters = [Fighter(Winner, 0, PvpOutcome.Win, new(5, 9000, 8000, true))],
            });

            var created = Assert.IsType<PvpSettleOutcome.Created>(first);
            var repeated = Assert.IsType<PvpSettleOutcome.AlreadySettled>(again);
            Assert.Equal(created.Results, repeated.Results);
            Assert.Equal(
                new[]
                {
                    new PvpFighterResult(Winner, 0, 0, PvpOutcome.Win, 2, 1, false, true, 3, 4, 1000, 4000, 1),
                    new PvpFighterResult(Loser, 1, 1, PvpOutcome.Loss, 1, 2, true, true, 2, 2, -100, 1900, 0),
                },
                created.Results);
            Assert.Equal(new PvpRecord(4, 4, 4000, 1, 0, 0, 1, 1), await store.ReadAsync(Winner, default));
            Assert.Equal(new PvpRecord(2, 0, 1900, 0, 1, 0, 0, 0), await store.ReadAsync(Loser, default));
            Assert.Equal(1L, Scalar(path, "SELECT COUNT(*) FROM pvp_matches"));
            Assert.Equal(2L, Scalar(path, "SELECT COUNT(*) FROM pvp_match_results"));
        });
    }

    [Fact]
    public async Task A_settlement_that_is_rolled_back_leaves_nothing_and_can_be_settled_afterwards()
    {
        await WithStoreAsync(async (store, scope, path) =>
        {
            var match = Guid.NewGuid();
            await using (var transaction = scope.Begin())
            {
                Assert.IsType<PvpSettleOutcome.Created>(await store.SettleAsync(Decided(match), default));
            }

            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM pvp_matches"));
            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM pvp_match_results"));
            Assert.Equal(new PvpRecord(3, 4, null, 0, 0, 0, 0, 0), await store.ReadAsync(Winner, default));

            Assert.IsType<PvpSettleOutcome.Created>(await SettleAsync(scope, store, Decided(match)));
        });
    }

    [Fact]
    public async Task Settling_outside_a_use_case_is_refused()
    {
        await WithStoreAsync(async (store, _, path) =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.SettleAsync(Decided(Guid.NewGuid()), default));
            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM pvp_matches"));
        });
    }

    [Fact]
    public async Task Daily_counts_are_the_counted_wins_and_losses_of_that_game_day_only()
    {
        await WithStoreAsync(async (store, scope, _) =>
        {
            await SettleAsync(scope, store, Decided(Guid.NewGuid()));
            await SettleAsync(scope, store, Decided(Guid.NewGuid()));
            // Another day, a draw, and a channel that keeps nothing: none of them count today.
            await SettleAsync(scope, store, Decided(Guid.NewGuid()) with { GameDay = Today.AddDays(-1) });
            await SettleAsync(scope, store, Decided(Guid.NewGuid()) with
            {
                OutcomeKind = PvpMatchOutcomeKind.Draw, Winner = null,
                Fighters =
                [
                    Fighter(Winner, 0, PvpOutcome.Draw, new(4, 6000, 0, false)),
                    Fighter(Loser, 1, PvpOutcome.Draw, new(2, 1700, 0, false)),
                ],
            });
            await SettleAsync(scope, store, Decided(Guid.NewGuid()) with
            {
                RecordsResult = false,
                Fighters =
                [
                    Fighter(Winner, 0, PvpOutcome.Win, new(4, 6000, 0, false)),
                    Fighter(Loser, 1, PvpOutcome.Loss, new(2, 1700, 0, false)),
                ],
            });

            Assert.Equal(new PvpDailyCounts(2, 0), await store.DailyCountsAsync(Winner, Today, default));
            Assert.Equal(new PvpDailyCounts(0, 2), await store.DailyCountsAsync(Loser, Today, default));
            Assert.Equal(new PvpDailyCounts(1, 0), await store.DailyCountsAsync(Winner, Today.AddDays(-1), default));
            Assert.Equal(new PvpDailyCounts(0, 0), await store.DailyCountsAsync(Winner, Today.AddDays(1), default));
        });
    }

    [Fact]
    public async Task A_win_extends_the_streak_a_loss_or_draw_ends_it_and_the_peak_stays()
    {
        await WithStoreAsync(async (store, scope, _) =>
        {
            async Task<int> Fight(PvpOutcome outcome)
            {
                var settled = await SettleAsync(scope, store, Decided(Guid.NewGuid()) with
                {
                    OutcomeKind = outcome == PvpOutcome.Draw ? PvpMatchOutcomeKind.Draw : PvpMatchOutcomeKind.Decided,
                    Fighters = [Fighter(Winner, 0, outcome, new(3, 3000, 0, outcome != PvpOutcome.Draw))],
                });
                return Assert.Single(settled.Results).WinStreak;
            }

            Assert.Equal(new[] { 1, 2, 3, 0, 1, 0, 1 },
                new[] { await Fight(PvpOutcome.Win), await Fight(PvpOutcome.Win), await Fight(PvpOutcome.Win),
                 await Fight(PvpOutcome.Loss), await Fight(PvpOutcome.Win), await Fight(PvpOutcome.Draw),
                 await Fight(PvpOutcome.Win) });
            Assert.Equal(new PvpRecord(3, 4, 3000, 5, 1, 1, 1, 3), await store.ReadAsync(Winner, default));
        });
    }

    [Fact]
    public async Task A_battle_that_keeps_nothing_is_written_without_touching_any_record()
    {
        await WithStoreAsync(async (store, scope, path) =>
        {
            var settled = await SettleAsync(scope, store, Decided(Guid.NewGuid()) with
            {
                OutcomeKind = PvpMatchOutcomeKind.Cancelled, Winner = null, RecordsResult = false,
                Fighters = [Fighter(Winner, 0, PvpOutcome.Draw, new(3, 3000, 0, false))],
            });

            Assert.Equal(0, Assert.Single(settled.Results).WinStreak);
            Assert.Equal(new PvpRecord(3, 4, null, 0, 0, 0, 0, 0), await store.ReadAsync(Winner, default));
            Assert.Equal(2L, Scalar(path, "SELECT outcome_kind FROM pvp_matches"));
        });
    }

    [Fact]
    public async Task A_settlement_no_battle_can_produce_is_refused_before_anything_is_written()
    {
        await WithStoreAsync(async (store, scope, path) =>
        {
            PvpMatchSettlementRecord[] refused =
            [
                Decided(Guid.Empty),
                Decided(Guid.NewGuid()) with { OutcomeKind = PvpMatchOutcomeKind.Cancelled },
                Decided(Guid.NewGuid()) with { Fighters = [Fighter(Winner, 0, PvpOutcome.Win, new(4, 4000, 1000, true)) with { Fled = true }] },
                Decided(Guid.NewGuid()) with { RecordsResult = false },
                Decided(Guid.NewGuid()) with
                {
                    Fighters = [Fighter(Winner, 0, PvpOutcome.Win, new(4, 4000, 1000, true)),
                        Fighter(Loser, 0, PvpOutcome.Loss, new(2, 1900, -100, true))],
                },
            ];
            foreach (var settlement in refused)
            {
                await using var transaction = scope.Begin();
                await Assert.ThrowsAsync<ArgumentException>(async () => await store.SettleAsync(settlement, default));
            }

            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM pvp_matches"));
        });
    }

    private static PvpMatchSettlementRecord Decided(Guid match) => new(
        match, RoomId: 5, ListenerPort: 10011, ChannelEnvironment: 24, PvpBattleMode.Individual,
        PvpMatchOutcomeKind.Decided, Winner: 0, RecordsResult: true, Today,
        new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
        [
            Fighter(Winner, 0, PvpOutcome.Win, new(4, 4000, 1000, true)) with { Kills = 2, Deaths = 1 },
            Fighter(Loser, 1, PvpOutcome.Loss, new(2, 1900, -100, true)) with { Team = 1, Kills = 1, Deaths = 2, Fled = true },
        ]);

    private static PvpFighterSettlement Fighter(CharacterId character, byte seat, PvpOutcome outcome, PvpSettlementDecision decision) =>
        new(character, seat, 0, outcome, 0, 0, false, decision);

    private static async Task<PvpSettleOutcome> SettleAsync(
        DomainTransactionScope scope, IPvpRecordDomainStore store, PvpMatchSettlementRecord settlement)
    {
        await using var transaction = scope.Begin();
        var outcome = await store.SettleAsync(settlement, default);
        await transaction.CommitAsync(default);
        return outcome;
    }

    private static async Task WithStoreAsync(Func<IPvpRecordDomainStore, DomainTransactionScope, string, Task> body)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"dfo-pvp-record-{Guid.NewGuid():N}.db");
            try
            {
                var scope = TestRequired.TransactionScope();
                var store = new SqlitePvpRecordDomainStore(new SqliteDatabase(path, scope));
                Execute(path, """
                    INSERT INTO accounts(account_id,m_id) VALUES(7,'pvp');
                    INSERT INTO characters(character_id,account_id,name) VALUES(71,7,'winner'),(72,7,'loser');
                    INSERT INTO character_pvp_record(character_id,grade,rating_grade) VALUES(71,3,4);
                    INSERT INTO character_pvp_record(character_id,grade,rating_grade,grade_experience) VALUES(72,2,0,2000);
                    """);
                await body(store, scope, path);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                File.Delete(path);
            }
        });
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
