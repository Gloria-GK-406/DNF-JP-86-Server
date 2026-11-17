using Dfo.Server.Application.Inventory;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Game;

public sealed partial class SelectCharacterLifecycleTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task An_added_registered_refill_runs_in_order_after_publication_and_false_does_not_stop_later_refills()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var added = new AddedRefill();
            var services = new ServiceCollection();
            services.AddSingleton(added);
            services.AddOnlineTicketRefill<AddedRefill, AddedRefill>("added.ticket-refill.online", 1, "RefillAddedTicket");
            await using var provider = services.BuildServiceProvider();
            var registered = Assert.Single(provider.GetServices<OnlineTicketRefillRegistration>());
            var first = new AddedRefill { Result = false };
            var last = new AddedRefill();
            var fixture = await Fixture.CreateAsync(ticketRefills:
            [new("last", 2, "RefillLastTicket", last), registered, new("first", 0, "RefillFirstTicket", first)]);
            first.OnRefill = session =>
            {
                Assert.True(fixture.Directory.IsInWorld(session));
                fixture.Ledger.Add("ticket:first");
            };
            added.OnRefill = _ => fixture.Ledger.Add("ticket:added");
            last.OnRefill = _ => fixture.Ledger.Add("ticket:last");

            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, default);

            Assert.Equal(new[] { "ticket:first", "ticket:added", "ticket:last" },
                fixture.Ledger.Where(value => value.StartsWith("ticket:", StringComparison.Ordinal)));
            Assert.True(fixture.Ledger.IndexOf("publish") < fixture.Ledger.IndexOf("ticket:first"));
            Assert.True(fixture.Ledger.IndexOf("ticket:last") < fixture.Ledger.IndexOf("quest"));
            Assert.Empty(fixture.Closer.Closed);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Unit")]
    public async Task Refill_failure_or_cancellation_stops_later_refills_and_preserves_selection_cleanup(bool cancelled)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Exception failure = cancelled ? new OperationCanceledException("refill cancelled") : new IOException("refill failed");
            var first = new AddedRefill();
            var failing = new AddedRefill();
            var last = new AddedRefill();
            var fixture = await Fixture.CreateAsync(ticketRefills:
            [new("first", 0, "RefillFirstTicket", first), new("failing", 1, "RefillFailingTicket", failing), new("last", 2, "RefillLastTicket", last)]);
            first.OnRefill = _ => fixture.Ledger.Add("ticket:first");
            failing.OnRefill = _ => throw failure;
            last.OnRefill = _ => fixture.Ledger.Add("ticket:last");

            var observed = await Record.ExceptionAsync(() => fixture.Handler.HandleAsync(fixture.Context, fixture.Command, default).AsTask());

            Assert.Same(failure, observed);
            Assert.Contains("ticket:first", fixture.Ledger);
            Assert.DoesNotContain("ticket:last", fixture.Ledger);
            Assert.DoesNotContain("quest", fixture.Ledger);
            Assert.Null(fixture.Directory.Find(fixture.AccountId));
            Assert.Equal(new[] { fixture.ConnectionId }, fixture.Closer.Closed);
            Assert.True(fixture.Ledger.IndexOf("ticket:first") < fixture.Ledger.IndexOf("cleanup"));
        });
    }

    private sealed class AddedRefill : IOnlineTicketRefillDomainService, IOnlineRefilledTicketRule
    {
        public int TicketItemId => 9001;
        public bool Result { get; init; } = true;
        public Action<SelectedSessionIdentity> OnRefill { get; set; } = _ => { };
        public ValueTask<bool> RefillOnlineTicketAsync(SelectedSessionIdentity session, CancellationToken ct)
        {
            OnRefill(session);
            return ValueTask.FromResult(Result);
        }
    }
}
