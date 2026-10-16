# 当前 JP86 接线与可复用底座

2026-10-16，只读源码核查；分支`task/next-round-20261013`，HEAD
`6e5b4ebd62b1e236aa273693c82d3dd094eec9a1`。未运行游戏、未变更服务和数据库。

## 当前缺口（CurrentImplementation）

- `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/EnterDungeonCommand.cs:14-47`
  已路由Tau、祭坛、大会、SAO、绝望塔、镜像、循环和深渊；没有赫拉斯/TimeSpiral专用路由。
- 对`Server/src`检索`TimeSpiral/BreakTrap/赫拉斯`，未见专用命令、内容源、领域服务或注册。
  `protocol/patterns/data/upstream-map.tsv`记录旧上游03AB；A21实际登记0408，详见A21子报告。
  文档中有协议身份不能等价于当前服务已注册处理。
- `Server/src/Dfo.Server.Application/Events/EventPlayerSnapshotProjection.cs:8-12`
  当前受调度地下城投影只生成Tau的3700，没有赫拉斯3900的对应状态。
  入口是否还需独立活动身份或其他客户端门，仍由ClientStatic/实机补证决定。
- `Server/src/Dfo.Server.Abstractions/World/DungeonRoomMechanisms.cs:9`
  当前专用房间机制类型仅Elevator。其房间代次、调度、结果模型可作设计参考，不代表陷阱已支持。
- `Server/src/Dfo.Server.Abstractions/UsageQuotas/UsageQuotas.cs:10`
  当前配额family包含Consumable/Premium/Mirror/Dimension/Despair/Altar/Tournament，没有赫拉斯类别。
- `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs:820`
  完整状态仍有固定`(3900,3)`。这仅证明旧投影含初始数值，不能证明已有入场扣次或跨日持久账本。
  PVF子报告另确认`0 3900 3 0`，数值吻合也不能替代账本实现。

## 可复用部分

| 能力 | 当前来源 | 赫拉斯实现时的边界 |
|---|---|---|
| 原子准入/费用/配额 | `Application/World/DungeonAdmissionDomainService.cs:24,131,211` | 由玩法规则给选项，沿用Prepare/Consume/Execute；不重写收费 |
| 普通地图/怪物/战斗/通关 | DungeonVisit/Run/Cell既有服务 | 现行3×3正常地图复用；特殊Boss身份与通关条件须另核对 |
| 房间状态与旧回调防串局 | RunId、RoomVersion、机制状态 | 若恢复陷阱，目标选择与消费绑定当前run/room并再次检查真实拓扑 |
| 物品发放/满包策略 | `Application/Inventory/ItemDeliveryDomainService.cs:26` | 按确认的奖励来源选择策略，不能复制上游未验证发奖 |
| 活动与在线可见性 | Event快照、Tau调度投影 | 扩展同一真源，避免只补登录或只显示活动 |

## 内容解析边界

`Infrastructure/Content/PvfDungeonDefinitionSource.cs:332-378`检查迷宫大小、连接数量及MAP坐标是否属于占用格；
坏配置保留诊断并在选用时拒绝，不能为旧5×5传送表全局放宽坐标检查。
当前3×3 DGN本身与客户端一致；旧global中的坐标不是当前迷宫的额外合法房间。

当前服务配置PVF：`artifacts/sao-ticket-20261014/Script.server.pvf`，本轮SHA256
`91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4`。
本报告无新的WireCaptured或ClientVerified结论。
