using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Testing;

// Linked only into the test projects that reference Dfo.Server.Protocol.
internal sealed partial class RecordedOutbound
{
    private IGameMessageSender? game;

    /// <summary>
    /// The protocol's CMD/NOTI sender over <see cref="Network"/>; created by name because it is internal to
    /// Dfo.Server.Protocol and this file is linked into test projects that cannot see it.
    /// </summary>
    public IGameMessageSender Game => game ??= (IGameMessageSender)Activator.CreateInstance(
        typeof(Dfo.Server.Protocol.ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game.GameMessageSender", throwOnError: true)!, Network)!;

    /// <summary><see cref="Explicits"/> as the Game plans they were registered from.</summary>
    public IReadOnlyList<GameExplicitMessageDelivery> GameExplicits => [.. Explicits.Select(call => ToGame(call.Delivery))];

    /// <summary><see cref="CallDeliveries"/> as Game plans.</summary>
    public IReadOnlyList<(NetworkConnectionId Direct, GameExplicitMessageDelivery Delivery)> GameCallDeliveries =>
        [.. CallDeliveries.Select(call => (call.Direct, ToGame(call.Delivery)))];

    private static GameExplicitMessageDelivery ToGame(ExplicitMessageDelivery delivery) => new(
        [.. delivery.RequiredDirectMessages.Cast<IGameServerMessage>()],
        [.. delivery.BestEffortTargets.Select(target => new GameExplicitMessageTarget(
            target.ConnectionId, [.. target.OrderedMessages.Cast<IGameServerMessage>()]))]);
}
