---
kind: rks-task-contract
revision: 1
state: ready
parent_controller: ../../multi-spec-map.md
---

# TaskContract: town-visible 非成员 party 投影

## Context

Spec-001 已证明当前 A21 ID `0x11` 主目录按 `0x0002 CNUser -> 0x0009 party association` 派生，且在
commit `2530f35b` 接受。基线只把 `0x0009` roster/settings/clear 发给 party 成员，导致已经持有远端
CNUser 的同城非成员无法建立 party 指针。

## Goal

复用现有 `0x0002` town presence 和 `0x0009` party codec，补齐 town-visible 非成员观察者投影；覆盖首次
选角、换区、创建/设置更新/accept/离队/解散、cleanup/断线/重选，并保证硬顺序、exact-session、同一
generation/revision 的并发收敛。结果不新增任何目录协议或持久状态。

## Scope

### In

- `PartyNotificationPlan` 或等价 Application 结果携带 mutation 时冻结的 town-visible exact observers。
- party 成员与 observer 受众分区；observer 只复用 `PartyRosterMessage.Refresh/Clear`。
- 选角/换区的 read-repair：相关 `0x0002` 成功后，按可见 representatives 的当前 party 去重发送 `0x0009`。
- entry read-repair 与 mutation 共用 party generation gate；旧 revision/generation、replacement session、
  delivery failure/retry不回滚或漏掉当前状态。
- 自动化覆盖领域、通知、handler 顺序与最小 client-state 因果模型；保持全部既有 party/town 行为。

### Out

- `0x023C/0x0283/0x0183`、ID `0x14` 候补窗、快速匹配、Story/Cycle 规则。
- 新目录仓储、party 全局枚举、per-channel directory gate、持久化。
- 原生 UI 的最终 `ClientVerified`（属于后继 Spec-003）。

## Constraints

- Application 不引用 Infrastructure；业务 mutation 仍由 Command/领域服务完成，Event/Notification 只投影。
- observer 范围必须与 `SessionPresenceScope.VisibleTownArea` 产生 CNUser 的范围相同；不扩大为 SameChannel。
- 全程保留 `SelectedSessionIdentity`，不得在发送前降级成 CharacterId。
- 远端角色 `0x0002` 成功后才能给该 observer 发其 party `0x0009`；反序必须通过 refresh read-repair 收敛。
- 成员既有 realtime/acceptance/endpoints/roster 顺序不得改变；observer 补帧在其后。
- 满员队继续投影，由客户端置灰；endpoint-only change 不广播 public roster。
- 新测试须先记录真实 RED，再做最小 GREEN；结构性重排不以伪造行为 RED 证明。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | party mutation 结果冻结准确、稳定、去重的 town-visible exact observers，跨 channel/town/area 不泄漏。 | Application 行为测试覆盖 create/update/accept/leave/disband、old+new anchors 和 replacement。 |
| SC-2 | member projection 保持，非成员按当前 snapshot 收到一次 type-0 或 type-3；离队者先 clear 再 observer refresh。 | Protocol notification 顺序/受众测试与既有 golden bytes。 |
| SC-3 | 首次选角和换区的双方都严格 `0x0002` 先、去重 `0x0009` 后；无 party 不加帧。 | handler/explicit-delivery 测试及 client-state model。 |
| SC-4 | entry read-repair 与 mutation 对同一 generation 串行，旧 revision/generation/replacement/failure 不制造回滚或永久漏帧。 | 可控并发、失败后同 revision 重试、新 observer 已提交 revision仍可读修复测试。 |
| SC-5 | 无 `0x023C` 新生产路径，Release build/聚焦与全量回归通过，完整独立审查 approved。 | registry/static search、逐项 TRX、build、diff-check、审查后新鲜验证。 |

## Authorization

根 TaskContract revision 2 的授权适用；允许生产/测试/协议与任务文档修改及本 Spec 一个本地接受提交。
