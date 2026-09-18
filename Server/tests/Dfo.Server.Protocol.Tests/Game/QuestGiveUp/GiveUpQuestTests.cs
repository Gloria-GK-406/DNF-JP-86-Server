using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.QuestGiveUp;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.QuestGiveUp;

public sealed class GiveUpQuestTests
{
    [Fact]
    public void The_client_give_up_packet_reaches_a_registered_quest_request()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0020);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);

        var request = decoder.Decode(GameMessageMetadata.Empty, [0x20, 0x00, 0xF8, 0x03]);

        var giveUp = Assert.IsType<GiveUpQuestRequest>(request);
        Assert.Equal((ushort)1016, giveUp.QuestId);
        Assert.Contains(provider.GetServices<MessageHandlerRegistration>(),
            value => value.MessageType == giveUp.MessageType);
    }

    [Fact]
    public void The_answer_names_the_abandoned_quest_or_reports_a_refusal()
    {
        Assert.Equal([0x01, 0xF8, 0x03], GiveUpQuestPayloadCodec.Encode(
            new GiveUpQuestAcknowledgement(true, 1016, 0)));
        Assert.Equal([0x00, 19], GiveUpQuestPayloadCodec.Encode(
            new GiveUpQuestAcknowledgement(false, 1016, 19)));

        Assert.Equal((ushort)1016, GiveUpQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, [0x1F, 0x00, 0xF8, 0x03]).QuestId);
        Assert.Throws<InvalidDataException>(() => GiveUpQuestPayloadCodec.Decode(
            GameMessageMetadata.Empty, [0x20, 0x00, 0xF8]));
    }

    [Fact]
    public async Task A_successful_give_up_refreshes_the_carried_and_offer_lists()
    {
        var session = SelectedSessionIdentity.Create(new AccountId(1), new CharacterId(60),
            new NetworkConnectionId("quest-give-up"), 1, 10011);
        var currentViews = new FixedCurrentViews();
        var entry = SelectCharacterPayloadTestData.Create();
        currentViews.Publish(session, entry with
        {
            World = entry.World with
            {
                Subject = new WorldContentSubject(
                    2, 0, 0, new Dictionary<int, int>(), [1016], [], [], 0, 0)
            }
        });
        var character = new CurrentCharacterStub
        {
            Id = session.CharacterId,
            Progress = new CharacterProgressionState(2, 0),
            Identity = new CharacterIdentityState(session.CharacterId, 0, [65], 0, 0)
        };
        var characters = CurrentCharacterStub.Domain(character);
        var content = new StubWorldContentEntryRuleService();
        var sender = new RecordingSender();
        var handler = new GiveUpQuestHandler(
            new OneSession(session), currentViews, characters,
            new SuccessfulGiveUp(), sender,
            new QuestOfferRefreshDomainService(content, new RecordingQuestLogs(), characters),
            NullLogger<GiveUpQuestHandler>.Instance);

        await handler.HandleAsync(
            new NetworkDispatchContext(new NetworkConnectionContext(session.ConnectionId,
                NetworkEndpointKind.Game, 10011)),
            new GiveUpQuestRequest(GameMessageMetadata.Empty, 1016), CancellationToken.None);

        Assert.Equal([
            typeof(GiveUpQuestAcknowledgement), typeof(AcceptedQuestProgressMessage),
            typeof(AvailableQuestListMessage)
        ], sender.Sent.Select(message => message.GetType()));
        Assert.Empty(Assert.IsType<AcceptedQuestProgressMessage>(sender.Sent[1]).Quests);
        Assert.Empty(Assert.IsType<WorldContentSubject>(content.LastSubject).CarriedQuestIds);
    }

    private sealed class SuccessfulGiveUp : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Assert.Equal((ushort)1016, Assert.IsType<GiveUpQuestCommand>(command).QuestId);
            return ValueTask.FromResult((TResult)(object)new GiveUpQuestOutcome(
                true, 1016, new QuestProgressSnapshot([])));
        }
    }
}
