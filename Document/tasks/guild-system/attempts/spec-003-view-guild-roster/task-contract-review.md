---
kind: rks-task-contract-review
contract_path: Document/tasks/guild-system/attempts/spec-003-view-guild-roster/TaskContract.md
contract_revision: 1
reviewer: /root/guild_roster_contract_review
selection_basis: 未参与本子契约建模、意图对齐或路径选择的独立评审者，按公会名单的只读交付边界评审
verdict: approved
---

# A21 公会成员名单子契约审查

核查了 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system` 内的[子契约第 1 版](TaskContract.md)，SHA-256 为 `6E106CBE1DEE332B6147E77BC33FF999639A3E1E4FD315B4CAB6DBF27A315EC0`。

权威来源包括用户的完整公会系统分期授权和本轮继续推进的明确回复、[根契约](../../TaskContract.md)、[分期记录](../../multi-spec-map.md)、仓库 `AGENTS.md` 和 [0x0043](../../../../../protocol/game/client-to-server/0x0043.md)、[0x008C](../../../../../protocol/game/client-to-server/0x008C.md)。对照了本地 `ServerS4A21/Server/DfoServer/Network/Builders/Guilds/GuildMemberPacketBuilder.cs` 和相邻的 `Handlers/GuildMemberHandler.cs`。上游约定提供首次全量请求与行格式的候选依据。

| 根意图 | 核对结果 |
| --- | --- |
| 名册按请求角色的当前公会关系读取，并显示在线或离线 | `in_scope`。AC-1 和 AC-2 覆盖默认名单、首次全量、当前会长与成员、实际频道及异常请求拒绝。 |
| 留存协议证据并区分实机观察、匹配版本上游和隔离验证 | `in_scope`。AC-3 和约束承接 SC-09、SC-10。 |
| 搜索、申请、审批、成员管理、备注写入、精确离线时长与其他阶段功能 | `preserved_upstream`。根契约仍负责后续阶段，本期身份和在线状态的验收明确可独立开展。 |
| 正式环境切换、PR 和发布 | `not_applicable`。本期授权为本地实现和隔离验证。 |

「员工操作」可与根契约的用语统一为「成员管理」。结合目标、验收和根任务归属，现有表述不改变本期的只读范围。

| 维度 | 证据等级 | 依据 | 影响 |
| --- | --- | --- | --- |
| quality | observed | 契约 AC-1、AC-2 给出名单内容、异常请求及所属公会隔离检查 | 分阶段验收可验证名单正确性和只读边界。 |
| efficiency | proxy | 已封存 spec-002 提供身份基础，协议页与匹配版本上游有两个入口的证据 | 可复用前置结果和已有线索，尚无本期交付耗时。 |
| cost | proxy | 本地工作区、隔离副本和现有客户端验收路径 | 可使用现有环境验证，实际工时与资源成本待实施后记录。 |
| cognitive burden | observed | 契约聚焦一个成员页，三条成功条件分别描述显示、拒绝和证据 | 验收者可按名单入口及不同角色状态检查结果。 |
| actual human participation | observed | 用户亲自确认上期频道设置，并要求继续下一项 | 已有分期推进决定。当前名单还需要 A21 界面验收。 |
