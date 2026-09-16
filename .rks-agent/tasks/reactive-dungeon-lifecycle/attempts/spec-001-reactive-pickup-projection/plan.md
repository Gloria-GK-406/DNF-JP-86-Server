---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 响应式权威拾取投影

## Planning Choice

采用 direct：改动围绕一个原子提交点、一个领域变更模型和一个协议投影组件展开，测试与实现可以保持在同一份计划中，无需额外 Task Unit 协调。

## Approach and Shared Decisions

- `IDungeonRunDomainService` 是共享拾取事实与语义接收者的唯一所有者；成功提交时递增运行期 revision 并返回 `DungeonPickupChange`。
- `PickUpGroundDropCommand` 在持久化入包/金币成功后提交带结果种类的变更，并把变更作为成功结果的一部分返回。失败和重复请求不产生变更。
- `DungeonPickupProjectionDelivery` 是唯一把领域角色身份解析为当前连接并编码 A21 0x0027 的协议边界。它验证冻结 generation 与当前 selected-session 一致，不把连接身份写回领域。
- `GetItemHandler` 只解析调用者、调用 CMD，并把结果交给投影组件；不再枚举其他参与者。
- 保留现有 18 字节 0x0027 布局。静态证据已证明它负责地面物状态而不是角色动作，因此不修改 UDP/P2P，也不补发观察者 CMD ACK。

## Work and Dependencies

| ID | Work | Requirements | Dependencies |
|---|---|---|---|
| T-001 | 在 World abstraction 中增加拾取结果种类、`DungeonPickupChange` 与 commit API；在 `DungeonRunDomainService` 中维护独立 runtime revision，并按当前 active participants 生成稳定角色接收者列表 | RQ-001, RQ-002, RQ-005 | diagnosis ready |
| T-002 | 扩展 `GroundPickupResult` 携带成功变更；让金币/物品路径只在持久化成功后调用新 commit API，重复/失败路径保持无变更 | RQ-001, RQ-003, RQ-005 | T-001 |
| T-003 | 新建 `DungeonPickupProjectionDelivery`：解析冻结参与者、过滤 replacement/离场/RunId 不匹配，向 picker 发送 ACK 和私有状态，向 observers 发送 0x0027；重写 `GetItemHandler` 调用它 | RQ-002, RQ-003, RQ-006 | T-001, T-002 |
| T-004 | 增加 Application 领域测试和 Protocol 双参与者测试，覆盖物品、怪物掉落、金币、重复、失败、离场、replacement 和其他 Run；保留 codec 字段断言 | RQ-001-RQ-006 | T-001-T-003 |
| T-005 | 更新协议/设计文档，记录 0x0027 与 P2P 动作边界、运行期变更所有权与实机验收步骤 | RQ-004, RQ-006 | T-003, T-004 |

## Verification

- 运行 `Dfo.Server.Application.Tests` 中 DungeonRun 定向测试，验证 revision、接收者和幂等。
- 运行 `Dfo.Server.Protocol.Tests` 中 GroundPickup codec/handler 定向测试，验证三类拾取、投影差异与旧会话过滤。
- Release 构建受影响项目并运行完整解决方案测试；确认无警告、无新增失败。
- 差异审查确认 `GetItemHandler` 不再出现其他参与者枚举，且 ClientHack/UDP 文件无改动。
