using System.Buffers;
using System.Text;

namespace Dfo.Server.Application.Authentication;

internal static class AuthenticationInputRules
{
    public static IReadOnlyDictionary<string, string> Validate(string loginId, string password, bool registration)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(loginId) || loginId.Length > 256 || loginId != loginId.Trim()
            || loginId.Any(c => c < 32 || c >= 127 || c == '?'))
            errors["loginId"] = "账号须为 1 至 256 个 ASCII 字符，不得含问号、控制字符或首尾空白。";
        var count = ScalarCount(password);
        if (count < (registration ? 8 : 1) || count > 128 || (registration && password.Contains('\0')))
            errors["password"] = registration ? "密码须为 8 至 128 个有效字符且不得含 NUL。" : "密码须为 1 至 128 个有效字符。";
        return errors;
    }

    private static int ScalarCount(string? value)
    {
        if (value is null) return -1;
        var remaining = value.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return -1;
            remaining = remaining[consumed..];
            if (++count > 128) return count;
        }
        return count;
    }
}
