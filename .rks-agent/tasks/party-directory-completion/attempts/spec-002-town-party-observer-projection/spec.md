---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: town-visible 非成员 party 投影

## Outcome

当客户端已通过 town presence 拥有某远端角色 CNUser 时，服务端也向同一 exact observer 投影该角色当前
party 的 `0x0009`，使 ID `0x11` 可从现有 CNUser/party state 派生公共列表。所有 mutation、entry、move
和 cleanup 最终收敛到当前 party 状态，且没有新目录 wire/state。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 领域 mutation 在 presence 撤销前冻结 town-visible exact observers；union old/new member anchors、去重并稳定排序。 |
| RQ-002 | member 与 observer 投影严格分区；create/update/accept/leave/disband 发正确 type-0/type-1/type-3 组合。 |
| RQ-003 | newcomer/mover 与 existing observers 的 town `0x0002` 成功后，按 current party 去重 read-repair `0x0009`。 |
| RQ-004 | 同一 generation 的 mutation/read-repair 共享串行所有者；旧 revision/generation 和 replacement 不覆盖当前状态。 |
| RQ-005 | delivery failure 不在必需发送完成前提交 revision；同 revision 可以重试，state-shaped observer 通知可安全重放。 |
| RQ-006 | 既有 party/town 协议、范围与阶段顺序不回退，且生产与注册表零新增目录意义 `0x023C`。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | create/settings/accept/leave/disband 的 plan 含预期 exact observers；不同 channel/town/area、not-standing、not-shared、stale session 排除。 |
| AC-002 | RQ-002 | 成员既有序列逐项不变；nonmember 收一次 full refresh；leaver clear-before-refresh；disband 每人最多一次 clear。 |
| AC-003 | RQ-003 | select/move 的 direct/publication 时间线证明每个 representative `0x0002` 先于对应 `0x0009`，同 party 只一次，无 party 零附加帧。 |
| AC-004 | RQ-004 | 并发测试证明 entry 旧候选不会越过新 mutation；新 observer 即使 revision committed 仍收到 current refresh；old exact 不命中 replacement。 |
| AC-005 | RQ-005 | 可控失败测试证明发送失败不提交 revision且同 revision 可重试成功；旧 committed revision 仍被抑制。 |
| AC-006 | RQ-006 | 既有 golden bytes、party/town/cleanup/selection 测试与全量 solution 通过；静态搜索确认没有新增 `0x023C` 目录生产路径。 |

## Invariants

- `TownObservers` 是一次 mutation 的不可变 exact-session 快照，不是长期 party 成员状态。
- `RefreshObservedParties` 在 gate 内重读 `IPartyDomainService.FindParty`；不发送调用前捕获的陈旧 snapshot。
- 每个 observer 只收到当前成员投影未覆盖的补帧；成员不因公共列表重复收到 roster。
- town departure 移除 CNUser 即足以使旧 scope 行消失，纯换区不广播 party clear。
- type-0/type-3 重放按 partyId 收敛；server gate 负责拒绝真正的旧 state。

## Test-value gate

- Protected outcome: 非成员公共列表能建立且在 lifecycle/竞态后不出现空白、重复、幽灵或跨 session 帧。
- Plausible regression: observer audience 漏发/过宽、`0x0009` 先于 `0x0002`、旧 revision 后到、离队 clear 顺序反转。
- Faithful oracle: 测试观察通知 recipient、消息种类/顺序、current snapshot 与最小 CNUser/party 客户端模型，
  不钉 constructor 或私有类形状。
- Marginal protection: 现有测试只覆盖成员与 town 两条独立链，无法证明跨链因果顺序和 nonmember 结果。
- Refactor tolerance: 只要 external notification timeline/state 一致，内部 planner/stream 可重构。
