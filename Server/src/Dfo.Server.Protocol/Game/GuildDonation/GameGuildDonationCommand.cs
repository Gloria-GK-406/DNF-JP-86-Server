using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildInfo;

namespace Dfo.Server.Protocol.Game.GuildDonation;

public sealed record GameGuildDonationCommand(NetworkDispatchContext Dispatch, GuildDonationRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildDonationCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildDomainService guilds,
    IGuildQueryDomainService query,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    TimeProvider clock) : ICommandHandler<GameGuildDonationCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildDonationCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var connection = command.Dispatch.Connection;
        if (!sessions.TryGetInWorld(connection.ConnectionId, out var actor))
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildDonationReply(0x22), ct).ConfigureAwait(false);
            return false;
        }

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        var (result, panel) = await units.WriteAsync(context, new("donate-guild-gold", [recipient]), async (_, token) =>
        {
            var donation = await guilds.DonateGoldAsync(actor.CharacterId, request.Gold, token).ConfigureAwait(false);
            if (donation.Status != GuildMutationStatus.Success)
                return (donation, (GuildInfoMessage?)null);

            var refreshed = await GuildInfoPanel.ReadAsync(query, actor.CharacterId, token).ConfigureAwait(false);
            var membership = refreshed.Membership
                ?? throw new InvalidOperationException("The donating member's guild panel could not be read.");
            return (donation, GuildInfoPanel.CreateMessage(membership, refreshed.Count));
        }, ct).ConfigureAwait(false);

        if (result is not { Status: GuildMutationStatus.Success, RemainingGold: { } gold } || panel is null)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildDonationReply(Error(result.Status)), ct)
                .ConfigureAwait(false);
            return false;
        }

        await outbox.RegisterBatchAsync(connection,
            [new GuildDonationReply(), GameInventoryDelivery.CarriedGoldNotification(gold, clock.GetUtcNow()), panel],
            ct).ConfigureAwait(false);
        return true;
    }

    private static byte Error(GuildMutationStatus status) => status switch
    {
        GuildMutationStatus.InsufficientGold => 0x0A,
        GuildMutationStatus.DonationTooLarge => 0x0C,
        GuildMutationStatus.InvalidDonationAmount => 0x1B,
        GuildMutationStatus.ContributionLimit => 0x5F,
        _ => 0x22,
    };
}
