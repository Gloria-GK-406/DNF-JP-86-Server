using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.TitleBook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class TitleBookMutationHandlerTests
{
    [Theory]
    [InlineData(false, true, false)] [InlineData(true, true, false)]
    [InlineData(false, false, false)] [InlineData(true, false, false)]
    [InlineData(false, true, true)] [InlineData(false, false, true)]
    public async Task Thin_mutation_handler_passes_the_complete_native_input_and_exact_physical_connection(bool store, bool complete, bool replaceMirror)
    {
        var connection = new NetworkConnectionContext(new("title-ack"), NetworkEndpointKind.Game, 10011);
        var dispatch = new NetworkDispatchContext(connection);
        var input = new TitleBookMutationRequest(new(1, store ? (ushort)0x019C : (ushort)0x019D, 0, 1),
            store, replaceMirror ? 0x314D4254 : 3, replaceMirror ? 9 : 13, 26596, 4, 10, complete);
        var commands = new Commands();
        await new TitleBookMutationHandler(commands).HandleAsync(dispatch, input, default);
        var command = Assert.IsType<GameTitleBookMutationCommand>(Assert.Single(commands.Seen));
        Assert.Same(dispatch, command.Dispatch); Assert.Same(connection, command.Dispatch.Connection); Assert.Same(input, command.Input);
    }

    [Fact]
    public void Actual_registration_has_six_unique_bindings_five_direct_wholes_and_no_seven_fine_types()
    {
        var services = new ServiceCollection(); new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        var rows = services.Where(x => x.ServiceType == typeof(CommandHandlerRegistration)).Select(x => (CommandHandlerRegistration)x.ImplementationInstance!).ToArray();
        var bindings = new[] { (65948, "GameTitleBookMutationCommand"), (65949, "GameTitleBookMutationCommand"),
            (65953, "GameAchievementTriggerCommand"), (65960, "GameQueryTitleBookCommand"), (66105, "GameSetTitleAppearanceCommand"), (66625, "GameWeeklyAttendanceCommand") };
        foreach (var (id, name) in bindings) {
            var row = Assert.Single(rows, r => r.ProtocolMessages?.Any(m => (int)m.MessageType == id) == true);
            Assert.Equal(name, row.CommandType.Name); Assert.Equal(CommandRouteKind.Protocol, row.Route); Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind);
            Assert.Empty(row.RegistrationExits ?? []); Assert.Equal(CommandSelfSettlementCapability.None, row.SelfSettlement);
        }
        foreach (var name in new[] { "StoreTitle", "WithdrawTitle", "EquipCarriedTitleReplacingMirror", "QueryTitleBook", "SetTitleAppearance", "ReconcileAchievement", "ClaimWeeklyAttendance" })
            Assert.DoesNotContain(typeof(ProtocolModule).Assembly.GetTypes(), t => t.Name == name + "Command" || t.Name == name + "CommandHandler");
    }

    private sealed class Commands : ICommandBus
    {
        internal List<object> Seen = [];
        public ValueTask<R> SendAsync<R>(ICommand<R> command, CancellationToken ct = default) where R : notnull
        { Seen.Add(command); return ValueTask.FromResult((R)(object)true); }
    }
}
