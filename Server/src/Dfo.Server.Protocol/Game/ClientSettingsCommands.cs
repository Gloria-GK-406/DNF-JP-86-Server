using System.Buffers.Binary;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

public sealed record ActivateClientHotkeyProfileCommand(ClientSettingsOrigin Origin, byte KeyType)
    : ICommand<bool>, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Origin.AccountId;
    public SelectedSessionIdentity? Session => Origin.SelectedSession;
}

public sealed class ActivateClientHotkeyProfileCommandHandler(IClientSettingsDomainService settings)
    : ICommandHandler<ActivateClientHotkeyProfileCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, ActivateClientHotkeyProfileCommand command, CancellationToken ct) =>
        ValueTask.FromResult(settings.ActivateHotkeyProfile(command.Origin, command.KeyType));
}

internal static class ClientSettingsPayload
{
    public static byte[] Decode(ReadOnlySpan<byte> body, int? exactLength = null)
    {
        if (body.Length < 4) throw new InvalidDataException("Settings require a four-byte length prefix.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(body);
        if (length > 65536 || length != body.Length - 4 || exactLength is { } exact && length != exact)
            throw new InvalidDataException($"Invalid settings length {length}; body={body.Length} expected={exactLength}.");
        return body[4..].ToArray();
    }
}
