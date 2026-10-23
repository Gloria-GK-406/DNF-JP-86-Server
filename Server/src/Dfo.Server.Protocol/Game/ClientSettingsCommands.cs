using System.Buffers.Binary;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

public enum ClientSettingKind { Main, QuickChat, Hotkeys }

public sealed record SaveClientSettingsCommand(ClientSettingsOrigin Origin, ClientSettingKind Kind,
    byte[] Payload, byte Bank = 0, ClientHotkeyUploadContext? Hotkeys = null)
    : ICommand<bool>, IAccountCommand, ISelectedSessionCommand
{
    public AccountId AccountId => Origin.AccountId;
    public SelectedSessionIdentity? Session => Origin.SelectedSession;
}

public sealed class SaveClientSettingsCommandHandler(IClientSettingsDomainService settings)
    : ICommandHandler<SaveClientSettingsCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, SaveClientSettingsCommand command, CancellationToken ct) =>
        command.Kind switch
        {
            ClientSettingKind.Main => settings.SaveMainAsync(command.Origin, command.Payload, ct),
            ClientSettingKind.QuickChat => settings.SaveQuickChatAsync(command.Origin, command.Bank, command.Payload, ct),
            ClientSettingKind.Hotkeys when command.Hotkeys is { } hotkeys && hotkeys.Origin == command.Origin =>
                settings.SaveHotkeysAsync(hotkeys, command.Payload, ct),
            _ => ValueTask.FromResult(false),
        };
}

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
