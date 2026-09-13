---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: A21 公共队伍目录阻塞闭合

## Outcome

建立固定 A21 上不含窗口混淆的队伍列表合同：明确 ID `0x11` 主目录和 ID `0x14` 候补窗分别消费哪些
协议/本地状态，裁决 matching-info、`0x023C`、`0x0183` 是否阻塞主目录，并给服务端后继 Spec 一套不含
猜测字段的最小实现输入。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 以构造器、factory ID、final vptr、资源和调用边界拆开 ID `0x11` 与 ID `0x14`。 |
| RQ-002 | 闭合 ID `0x11` 的完整网络态 producer/consumer、硬顺序、筛选/去重与行字段。 |
| RQ-003 | 闭合 matching-info 的真实来源和 `0x023C/0x0283/0x0183` 的归属，旧误判不得继续作为 blocker。 |
| RQ-004 | 对照当前服务端，给出可开工/不可开工 verdict、最小生产边界、并发/生命周期风险和实机剩余。 |
| RQ-005 | 所有结论绑定当前 B1/dstr/服务端基线，保留 Confirmed static、High 与 runtime frontier 分级。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 两个窗口的 factory ID、constructor、vptr、资源与消费者不再交叉归属。 |
| AC-002 | RQ-002 | 主目录被证明只依赖 `0x0002 CNUser + 0x0009 party`，且明确 leader CNUser 先于 roster refresh 的不可交换顺序。 |
| AC-003 | RQ-003 | matching-info 被证明为本地资源；`0x023C` 被归入候补窗；`0x0183` 不再阻塞主目录。 |
| AC-004 | RQ-004 | 当前服务端缺口收敛为 town-visible 非成员 `0x0009` 投影及其生命周期，不新增 `0x023C` codec/目录仓储。 |
| AC-005 | RQ-005 | 输入哈希、报告哈希、关键地址和剩余实机矩阵可复核，独立审查无 P1/P2。 |

## Evidence Boundary

- 静态证据可以批准后继生产实现，但不能把原生 UI 行为标成 `ClientVerified`。
- 旧单独注入 `0x0009` 的负结果继续有效，但原因只标为 High：当时未回读 leader CNUser，也未命中正确
  `flush/render`。
- ID `0x14` 候补窗的真实非空 `0x023C` 样本与业务值域保留为后续独立任务，不属于主目录 blocker。

