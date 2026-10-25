using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed class MarriageProtocolRegistrationTests
{
    [Theory]
    [InlineData(66544, "GameWeddingProposalCommand")] [InlineData(66545, "GameWeddingResponseCommand")]
    [InlineData(66546, "GameWeddingGiftCommand")] [InlineData(66547, "GameWeddingPartnerCommand")]
    [InlineData(66549, "GameWeddingLovePointCommand")] [InlineData(66550, "GameWeddingLetterCommand")]
    [InlineData(66551, "GameWeddingChurchEnterCommand")] [InlineData(66552, "GameWeddingCommentCommand")]
    [InlineData(66553, "GameWeddingCommentCommand")] [InlineData(66554, "GameWeddingCommentCommand")]
    [InlineData(66557, "GameHousingCommand")] [InlineData(66564, "GameCapsuleCommand")]
    public void Each_marriage_message_has_one_closed_whole_route_and_no_extinct_fine(int id, string name)
    {
        using var f = new ShopFixture();
        var messages = f.Services.GetRequiredService<MessageHandlerRegistry>();
        Assert.True(messages.TryGet((ClientMessageType)id, out var route));
        Assert.Equal(MessageHandlerRouteKind.ProtocolCommand, route!.Kind);
        Assert.Equal(name, route.ProtocolCommandType!.Name); Assert.False(route.IsTelemetry);
        var command = f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(route.ProtocolCommandType);
        Assert.Equal(CommandRouteKind.Protocol, command.Route); Assert.Equal(CommandExecutionKind.Direct, command.ExecutionKind);
        Assert.All(route.HandlerType.GetConstructors().SelectMany(x => x.GetParameters()), x => Assert.Equal(typeof(ICommandBus), x.ParameterType));
        Assert.Single(command.ProtocolMessages!, x => (int)x.MessageType == id);
        Assert.Equal(name is "GameHousingCommand" or "GameCapsuleCommand" or "GameWeddingLovePointCommand"
            ? new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch } : [], command.RegistrationExits ?? []);
        string[] extinct = ["MarriageProposalCommand", "MarriageCeremonyCommand", "PrepareMarriageChurchCommand", "EnterMarriageChurchCommand",
            "WeddingCommentCommand", "WeddingGiftCommand", "WeddingLetterCommand", "WeddingLovePointCommand", "HousingCommand", "CapsuleCommand"];
        Assert.DoesNotContain(typeof(Dfo.Server.Protocol.ProtocolModule).Assembly.GetTypes(), x => extinct.Contains(x.Name) || extinct.Any(n => x.Name == n + "Handler"));
        var registrations = f.Services.GetServices<CommandHandlerRegistration>().ToArray();
        Assert.DoesNotContain(registrations, x => extinct.Contains(x.CommandType.Name));
        foreach (var retained in new[] { "InitializeHousingCommand", "AdvanceMarriageWeddingCommand", "CompleteWeddingCleanupCommand" })
            Assert.Contains(registrations, x => x.CommandType.Name == retained && x.Route == CommandRouteKind.LegacyFineGrained);
    }
}
