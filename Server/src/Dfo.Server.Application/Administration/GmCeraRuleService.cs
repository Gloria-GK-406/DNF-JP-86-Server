using Dfo.Server.Abstractions.Accounts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

internal sealed class GmCeraRuleService(IAccountWalletRuleService wallets) : IGmCeraRuleService
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public GmCeraInput Normalize(GmCeraInput input)
    {
        if (input.AccountIds is not { Length: >= 1 and <= 100 } || input.AccountIds.Any(x => x <= 0))
            throw Invalid("请明确选择 1..100 个账号，账号 ID 必须为正整数。");
        if (input.Currency is not ("Cera" or "TokenCera" or "HappyTokenCera")) throw Invalid("币种无效。");
        if (input.Amount <= 0) throw Invalid("每账号增量须为 1..2147483647 的整数。");
        var reason = input.Reason?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (reason is not { Length: >= 1 and <= 256 } || reason.Any(x => char.IsControl(x) && x is not ('\n' or '\t')))
            throw Invalid("发放原因须为 1..256 个字符，不能含无效控制字符。");
        try { _ = Utf8.GetByteCount(reason); } catch (EncoderFallbackException) { throw Invalid("发放原因含无效字符。"); }
        return new(input.AccountIds.Distinct().Order().ToArray(), input.Currency, input.Amount, reason);
    }
    public string PayloadHash(GmCeraInput input) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(JsonSerializer.Serialize(input))));
    public GmCeraDecision Decide(AccountCeraBalances before, GmCeraInput input)
    {
        if (input.Currency is not ("Cera" or "TokenCera" or "HappyTokenCera")) throw Invalid("币种无效。");
        try { return new(before, wallets.Add(before, Enum.Parse<AccountCurrency>(input.Currency), input.Amount)); }
        catch (WalletException error) when (error.Rejection == WalletRejection.BalanceLimit)
        { throw Invalid("发放后余额超过 2147483647，请重新核对。"); }
        catch (Exception error) when (error is WalletException or InvalidDataException or ArgumentException)
        { throw Invalid("币种、增量或发放后的余额无效，请重新核对。"); }
    }
    private static GmCeraException Invalid(string message) => new(GmCeraRefusal.Invalid, message);
}
