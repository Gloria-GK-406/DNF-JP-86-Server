---
kind: rks-review
verdict: approved
---
# 固定等级强化／增幅券结果审查

## 审查目标与范围

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`。
- 基线：`c8f25b6ce2cd6bce01a00fe672405788c9d69641`；审查基线至当前工作树完整 tracked diff、未跟踪的 `EquipmentUpgradeRuleService.Tickets.cs`，以及本任务契约、spec、执行记录和人工验收清单。
- 当前源码目标：`verification/source-fingerprint.json` 声明的 SHA-256 `1dfc2dba02efc2a02659a3980d2457ff06e3204a45bdf10a3dad8528608758af`。独立逐项核对其 1,567 个源码／检查输入文件，全部文件哈希匹配，0 个不一致。
- Governing：`TaskContract.md` revision 1、`spec.md` RQ-001—RQ-006。采用 reviewing-work 的 `references/code.md`、`references/deliverable.md`，并遵守仓库 AGENTS 中分层、事务、通知及证据边界约束。
- 本审查使用独立 reviewer 上下文，不修改生产或测试，不启动并发 .NET 构建／测试；未配置或要求 challenge set。

## 结论

当前目标未发现需要修复的阻塞缺陷，完整变更的独立结果审查为 **approved**。此结论批准所识别的代码及文档目标，不代替随后新鲜的完成验证、本地提交、部署检查或用户实机验收。

## 关键核对

| 范围 | 实际代码与证据 | 判断 |
|---|---|---|
| 券识别与 NPC 分流 | `EquipmentReinforcementDomainService` 从所选物理材料槽读取物品后先调 `QuoteTicketAsync`；`IsTicket` 为 true 时使用该报价，包括拒绝结果。`TicketOnly` 测试让 NPC 表读取直接抛异常，非 fixed／非 -1 券仍得到明确 `UnsupportedContent` | 已识别券不会回退 NPC 成本或等级表；普通非券仍走既有报价 |
| PVF 声明 | Infrastructure 使用两个静态具名字段；精确读取四项并检查 token 类型、等级 0..31、百分比 0..100；重复、双类别和多余项有合成测试。四种真实券声明有 PVF 检查 | 缺失与损坏分开；未知合法模式保留后明确拒绝 |
| 装备与券准入 | 券规则保留装备类型、禁用内容、耐久、封装和异次元准入；领域检查目标身份及双锁，券数量、双锁、期限、`CanUseAsync` 和显式保护槽。增幅使用既有 `CheckAmplificationAsync` 校验已辨识且有效的异次元属性 | 前置拒绝不支付；无耐久首饰可用；普通强化券不能作用于红字目标 |
| 成功／概率失败 | `TicketRule` 的固定目标与百分比独立于 NPC；100%/90%/0%边界测试。effect 计划成功为消费一张券并设置目标等级，失败仅消费券，无金币、保护、损毁及奖励 effect | 正确覆盖较高原等级；失败保持装备及其等级；不使用 NPC 失败惩罚 |
| 事务与身份保持 | `ReinforceItemCommand` 保持 Transactional 与账号／用户 Exclusive 注册；整批走 `ApplyEffectsDomainService`，装备 effect 按 UID 和 expected item 校验，再由实例仅设置强化位。SQLite 触发器故障测试检查券、装备及缓存回滚；重建容器后检查 UID、等级、附魔和其他属性 | 与既有事务路径一致，无独立资产写入或身份重建 |
| 提交、通知与重放 | handler 在 `commands.SendAsync` 返回后发布通知；receipt 取最终槽与当前金币，通知含 ACK、占用／清空槽及金币。三个 ticket handler 场景检查发帧前持久状态，重复同请求不重扣／重抽，新 seq 才再次使用 | 实际处理链支持立即库存投影及已有请求账本保护 |
| 既有行为 | 非券计划保留 SpendGold、NPC +1、失败服务、保护及返材料路径。首轮完整 Application TRX 中强化／增幅／装备 effect／升级规则相关 179 项全部通过 | 未发现 NPC 或现有保护路径回归 |
| 文档证据边界 | `spec.md`、人工清单把当前 PVF、ServerS4A21 失败规则与用户实机验收分开。独立读取参照服 `InventoryItemUpgradeService.ResolvePenaltyType`/`ApplyPenalty`：Ticket 返回 penalty 1，通常保留原等级；`ItemUpgradeModels.FromTicket` 为一张材料、零金币 | 文档没有把 UpstreamReference 或自动检查升级为 ClientVerified |

## 实际检查证据与完成边界

- 既有定向证据：`tickets-related.trx` 为 125 通过、0 失败；`tickets-content.trx` 为 25 通过、0 失败。相关 TRX 是执行证据，不能代替对实际源码的审查。
- 新鲜 Release 构建日志为 0 warning、0 error；已完成的 final Pvf 为 80 通过，Infrastructure 为 750 通过、0 失败，另有 2 个未通过项为跳过而非失败。
- 首轮完整 Application 结果保存在 `first-full-Application.trx`：2,626 项、2,625 通过、1 失败。唯一失败为 `NotificationDeliveryTests.The_deadline_a_stalled_recipient_runs_into_is_the_configured_one` 的墙钟上限断言，两个 50ms deadline 测得 3,877ms。通知生产及该测试相对基线无改动；现有生产仍执行 `CancelAfter(options.PerRecipientTimeout)`。`notification-deadline-isolated.trx` 原样隔离复验 1 通过、0 失败。现有证据未证明该异常由券改动引起，也不将首轮改称“完整全绿”或“已确认基线失败”。
- 同一源码目标的完整 Application 重跑已结束：`final-Dfo.Server.Application.trx` 为 2,626 通过、0 失败，`final-checks.json` 记录 exitCode 0。报告形成时余下项目的完整串行回归仍在运行，最终验证由主代理继续确认。部署前须完成契约规定的新鲜检查，保留首轮异常及复验记录。若最终出现相关缺陷，必须处置后再通过完成门；若修改已审源码，须对变更后的目标重新独立审查。
- 本地提交、备份／部署、监听／ready 和 T01—T04 用户验收尚属于后续执行／验证动作。此批准不宣称这些已完成，也不宣称本功能 ClientVerified。

## Findings

无已证明的阻塞代码或交付物缺陷；无待修复 findings。

## 完成与部署证据补充审查（2026-10-01）

```yaml
kind: rks-review
verdict: approved
```

本次补充审查针对新增 `verification/completion-evidence.md`、`deployment.md`、`deployment-evidence.json`、`test-summary.json`、`spec-result.md`，以及 `Document/tasks/item-effects-unification/human-test-list.md` 的固定券入口和 `protocol/game/client-to-server/0x0050.md` 的 CurrentImplementation 段。生产／测试目标未变：重新核对源码清单 1,567 文件仍全部匹配 `1dfc2dba02efc2a02659a3980d2457ff06e3204a45bdf10a3dad8528608758af`，HEAD 仍为基线。补充审查适用 Deliverable 与原契约/spec，不另设阶段。

独立读取八份最终 TRX，合计 5,236 通过（80+2,626+26+50+750+86+392+1,226）、0 失败、2 跳过，与 test-summary 一致；final-checks 的十项命令全部 exitCode 0，Release 与 Probe 构建均零警告／错误。首轮 Application 异常仍有原始 TRX/log，原样隔离复验及全套通过证据同时保留，文档没有声称已证明调度根因。

静态字段审计：读取并原样复跑 `python Document/tasks/pvf-typed-schema/verification/check-field-access.py`，exitCode 0，PASS 检查 1,021 个源码文件；现有 `verification/field-access-audit.log` 与本次结果一致。脚本明确检查固定字符串 tag lookup／comparison／helper access，其语法排除动态查询和字段值枚举；此结果不扩张为全部 PVF 语义验证。

部署工件位于 `E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-upgrade-tickets-20261001`。独立读取部署 JSON、监听及 stdout/stderr，逐项核对 11 个发布 Dfo DLL 与当前 Release 构建哈希，0 差异；正式配置匹配原 runtime 与声明哈希，PVF 匹配声明哈希。实时核对 PID15484 的可执行路径为本发布目录，7001/10010/10011/18080 四个 TCP 监听均由该进程拥有，bootstrap 返回 HTTP200、status ready。stderr 为空，stdout 未见 fail 级别消息。

独立通过 SQLite 只读连接重算 `pre-deploy.db` 与正式数据库的全部 48 表排序行内容摘要（包含 SQLite 系统表），均与切换前快照完全一致，quick_check 均为 ok；部署前后券／保护券汇总相同，smoke 副本的前后表摘要相同。没有输出配置机密或资产库内容。

新人工入口保留 H01—H07 的既有验收状态并说明旧 PID 为历史部署；0x0050 新段没有修改线级布局或宣称新增实帧，固定券仍等待 T01—T04 GUI／重登验收。完成与结果文档明确是接受前候选，`acceptedCommit` 为 null，唯一结果提交由后续 Git 接受操作外部返回，没有自引用提交 SHA。本补充批准当前新增文档／证据目标，无具体阻塞缺陷；不升级为 ClientVerified，也不代替尚未执行的 Git 接受操作。

补充目标的文件 SHA-256：

| 文件 | SHA-256 |
|---|---|
| verification/completion-evidence.md | bd72aa1ccb748fe381ff00cd6b6ce2854cb7948464ff5ff32df70d938ad6d1aa |
| verification/deployment.md | 4ef8c90377ccc5bae02e0b30aa603904088d3422998b3c81fcbf058fad1ea222 |
| verification/deployment-evidence.json | 64a8a1973ed18e7d7ddab49e37239b4a8acc6e4fa53040872197522f84894f4a |
| verification/test-summary.json | 2cc8424f30ef12d265d17eb885353596d9b6ec88f457b5275c8103cf935d3159 |
| spec-result.md | de8d670d38e6dc838f85f281dacf52d4db534999e19e4d0f26ab2e0060056e4c |
| Document/tasks/item-effects-unification/human-test-list.md | 63b0017067fdabcd43c3ab3fbac212abbbbd0bdf00e0180cfbcd5d453993dc85 |
| protocol/game/client-to-server/0x0050.md | 5dfeefd14d10c0fa2391b38349f8ece230fdfb0443c1195c345d9e818e816cd6 |
