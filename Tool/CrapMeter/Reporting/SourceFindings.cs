using System.Globalization;
using System.Text;

namespace CrapMeter.Reporting;

// Human source review from addbeb52, 2026-09-08. Apply only to the exact inspected
// source/test contents; future edits require fresh review, never stale recommendations.
internal static class SourceFindings
{
    private sealed record Finding(string Symbol, string Path, string Hash, string EvidencePath, string EvidenceHash,
        string Evidence, string Interpretation, string Option);

    private static readonly Finding[] Findings =
    [
        new("Dfo.Server.Protocol.Game.DungeonSelection.EnterSelectDungeonHandler.HandleMessageAsync",
            "Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs",
            "D081F74BBF44C7A3D3264A71BFC07EFE27C270E9D6EA80F65DFDBA34FF92FF74",
            "Server/tests/Dfo.Server.Protocol.Tests/Game/RosterScreenQueries/DungeonSelectionCodecTests.cs",
            "5243EF0DDC3F46F9F4BE14CB1608686BAD252C43E1D7D41F3FF44A7AB58E7B8F",
            "源码 379–393 行拒绝未发布会话；433–449 行决定是否追加选图状态，基础帧有固定顺序；456–475 行发送并在异常时记录后重抛。所审阅测试文件 21–44 行仅检查 decoder 的输入长度/字节，没有检查这里的会话判断与发送序列。",
            "优先级高（推断）：影响进入选图界面的可见响应，分支组合比单帧编码更容易漏测。变更频度未知；影响面为选图请求，未据此认定已有缺陷。",
            "test-first：经公开 handler 入口覆盖无会话、未发布、教程延迟状态、普通选图，以及发送异常；断言帧顺序与数量。预计中等成本（需会话与发送替身）；这些保护通过后才考虑拆分组帧与发送。"),
        new("Dfo.Server.Infrastructure.Content.PvfTutorialRewardDefinitionSource.Read",
            "Server/src/Dfo.Server.Infrastructure/Content/PvfTutorialRewardDefinitionSource.cs",
            "741BD4F988ED5912AC3A49096974E635FA7A4A72753A3F16FBD61F6EE776CCB4",
            "Server/tests/Dfo.Server.Application.Tests/Game/ExitFlushHandlerTests.cs",
            "47C07909818CA8E3EF3D7AE8BCE4F43A85D00035008ABC66C5F052835D199D27",
            "源码 50–89 行按标签和三整数一组解析奖励，遇其他标签即停止，丢弃非法 flag/count，合并重复 flag。GetAsync 在 31–36 行惰性调用它。所审阅测试 268–289 行使用 NoRewards/OneReward/TwoOfOneReward 替身，不执行 PVF 解析。",
            "优先级中高（推断）：奖励规则读错可能影响发物品，但此方法本身只读档案，CRAP 不证明发生过错发。变更频度未知；直接影响教程奖励。",
            "test-first：先经真实内容源校验现有档案的奖励；再为未闭合标签、残缺三元组、重复 flag、无效数量构造小档案用例。预计中等成本（档案夹具）；有行为保护后再考虑将 token 解析提成纯函数。"),
        new("Dfo.Server.Application.Inventory.InventoryEntryAssembly.ParseLockContainerType",
            "Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs",
            "F12AF46B698D88FD73D904DFBE880634E56228B38997C579F679E99ED5E40F10",
            "Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs",
            "984CFC9AA9433895E79D6CBBAD86185664E028D47058273534B4698FA993B3E4",
            "源码 691–708 行是 14 个容器编号加默认抛错的 switch 映射，597 行由读锁记录路径调用。所审阅测试 119–132 行提供的是 Equipment 与 AccountCargo 两类锁记录；它不是覆盖全部整数映射的用例。",
            "优先级中（推断）：分数主要受宽映射表及未走分支影响，源码没有复杂嵌套。影响面为入场读取锁记录；变更频度未知，不因 C 高就要求结构重写。",
            "test-first / defer-refactor：经入场装配入口参数化验证 14 个合法编号与非法编号拒绝；预计低到中等成本（复用库存夹具）。保留可读 switch，只有增加映射导致维护问题时再比较表驱动方案。"),
    ];

    public static void Append(StringBuilder text, ReportInput input)
    {
        text.AppendLine("### 源码核查后的优先项（独立于机械分数排序）");
        text.AppendLine();
        text.AppendLine("核查基准：2026-09-08 / addbeb52。仅在被审阅源码和测试文件的 SHA256 仍匹配时展示；数值取本次运行。证据置信度为代码/测试内容已核实，业务优先级与成本属于推断。其他 Top N 仍只是待核查候选。");
        text.AppendLine();
        int shown = 0;
        foreach (Finding finding in Findings)
        {
            var hashes = input.Manifest.Before?.Files;
            if (hashes is null || hashes.GetValueOrDefault(finding.Path) != finding.Hash
                || hashes.GetValueOrDefault(finding.EvidencePath) != finding.EvidenceHash) continue;
            var record = input.Records.FirstOrDefault(r => r.Member.QualifiedSymbol == finding.Symbol && r.Crap > 30);
            if (record is null) continue;
            shown++;
            double percentile = (double)input.Records.Count(r => r.Crap is { } c && c <= record.Crap)
                / input.Records.Count(r => r.Crap is not null);
            text.AppendLine($"#### {shown}. `{finding.Symbol}`");
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"实测：C={record.Member.Complexity}；Cov={record.Coverage:P2}；CRAP={record.Crap:0.####}；分位={percentile:P2}；above-warning；{record.State}。范围 `{record.Member.Project}` / `{record.Member.Container}`。");
            text.AppendLine();
            text.AppendLine($"源码 `{finding.Path}:{record.Member.StartLine}`（SHA256 `{finding.Hash}`）；对照测试 `{finding.EvidencePath}`（SHA256 `{finding.EvidenceHash}`）。");
            text.AppendLine();
            text.AppendLine("已观察：" + finding.Evidence);
            text.AppendLine();
            text.AppendLine("推断与限制：" + finding.Interpretation);
            text.AppendLine();
            text.AppendLine("可选改进：" + finding.Option);
            text.AppendLine();
        }
        if (shown == 0) text.AppendLine("本次没有符合原核查快照且仍越线的优先项；请对当前候选重新核查源码及断言。");
        text.AppendLine();
    }
}
