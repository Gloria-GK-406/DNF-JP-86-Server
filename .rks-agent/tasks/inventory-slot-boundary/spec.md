---
kind: rks-spec
contract:
  path: .rks-agent/tasks/inventory-slot-boundary/TaskContract.md
  revision: 1
state: ready
---

# Specification: 原子游戏槽位与业务槽位边界

## Why and Outcome

客户端用 `list/space + absolute slot` 标识位置，而业务关心的是快捷栏、随身装备、材料、穿戴位等逻辑空间和其中的局部位置。交付后，协议坐标只存在于网络边界，业务坐标只存在于 CMD、领域与业务结果中；一个权威映射负责双向转换，任何不能映射的坐标都以显式异常终止当前请求。

## Definitions

- **GameSlot**：不可分割的协议地址，包含游戏空间枚举和该空间内的绝对槽位。
- **BusinessSlot**：不可分割的领域地址，包含业务空间枚举和该空间内的局部槽位。
- **结构映射**：不考虑当前角色是否已扩容，只判断客户端坐标是否属于已定义空间；扩容开放范围仍属于业务合法性判断。
- **普通物品 CMD**：本任务中的移动、使用、丢弃、出售、修理和分解。

## Scope

覆盖六条普通物品 CMD 的请求解码、协议 Handler、ICommand、CMD Handler、领域接口、领域结果和 CMD 响应编码，以及它们共同依赖的槽位合法性和持久化适配。`contextValue` 保持不透明且不改变。虚拟货币槽可以继续由状态投影专用逻辑产生，但不能作为普通物品 CMD 的业务槽位。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 每个槽位地址必须由原子值对象表达；任何业务 API 都不得以两个独立参数表达一个位置。 |
| RQ-002 | Decoder 产出的请求用 `GameSlot` 保存地址；协议 Handler 必须在创建 ICommand 前把它转换为 `BusinessSlot`。 |
| RQ-003 | ICommand、CMD Handler、领域接口、领域实例和业务回执只能携带 `BusinessSlot`，不得携带游戏空间枚举或客户端绝对槽位。 |
| RQ-004 | 需要回写位置的协议 Handler 必须在构造 CMD 响应前把 `BusinessSlot` 转回 `GameSlot`；Encoder 只编码 `GameSlot`。 |
| RQ-005 | 一个无 `TryXxx` 降级入口的规则服务必须同时提供 `GameSlot -> BusinessSlot` 和 `BusinessSlot -> GameSlot`，并保证受支持地址往返恒等。 |
| RQ-006 | 未知游戏空间、结构越界、保留槽、普通 CMD 指向虚拟货币槽，以及无法反向表达的业务槽必须抛出包含源坐标的专用映射异常。 |
| RQ-007 | 网络接收边界必须记录并隔离映射异常到当前连接；异常不得被转换成普通的出售、修理、分解等业务拒绝。 |
| RQ-008 | 业务槽位规则先验证“该业务空间能否容纳此物品”，行为规则再验证“该物品能否执行当前行为”；两类判断不得用协议区段数字代替。 |
| RQ-009 | 当前数据库的 `list_type/slot_index` 格式必须保持可读写；持久化适配不得让原始游戏坐标重新进入 CMD 或领域接口。 |
| RQ-010 | 所迁移链路中必须删除旧的分离参数、私有区段偏移和重复换算；不存在新旧两条可运行路径。 |

## Mapping Contract

普通物品坐标至少支持以下结构映射；局部槽位均从零开始：

| GameSlot | BusinessSlot |
|---|---|
| `Main, 3..8` | `QuickSlot, 0..5` |
| `Main, 9..64` | `CarriedEquipment, 0..55` |
| `Main, 65..120` | `Consumable, 0..55` |
| `Main, 121..176` | `Material, 0..55` |
| `Main, 177..232` | `Quest, 0..55` |
| `Main, 233..288` | `ExpertMaterial, 0..55` |
| `Main, 289..351` | `AvatarEmblem, 0..62` |
| `Avatar, 0..209` | `Avatar, 0..209` |
| `PersonalCargo, 0..151` | `PersonalCargo, 0..151` |
| `Equipment, 0..10` | `WornAvatar, 0..10` |
| `Equipment, 11..23` | `WornEquipment, 0..12` |
| `Equipment, 24` | `WornCreature, 0` |
| `Equipment, 25..27` | `WornCreatureEquipment, 0..2` |
| `Equipment, 29` | `WornEquipmentExtension, 0` |
| `Pet, 0..139` | `CreatureInventory, 0..139` |
| `Pet, 140..188` | `CreatureEquipment, 0..48` |
| `Pet, 189..239` | `CreatureConsumable, 0..50` |
| `AccountCargo, 0..63` | `AccountCargo, 0..63` |
| `GuildMedal, 0..48` | `GuildMedal, 0..48` |
| `GuildMedal, 49..97` | `GuardianGem, 0..48` |

`Main,0..2`、`Main,352..359`、`Equipment,28`、当前未建模的 title/quick-slot 独立 list type 以及所有未知范围，不是普通物品 CMD 的合法结构地址。

## Boundary Flow

```text
wire fields
  -> GameSlot (decoder)
  -> BusinessSlot (protocol handler, before ICommand creation)
  -> ICommand / CMD Handler / domain / receipt
  -> GameSlot (protocol handler, before response construction)
  -> wire fields (encoder)
```

移动请求的起点和终点分别遵循完整流程；回复所回显的位置必须来自业务结果或业务命令中的 `BusinessSlot` 反向映射，不能保存一份旁路原始字段用于回包。

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001,RQ-002,RQ-003 | 对六条请求进行生产代码引用扫描时，ICommand 及其 Handler/领域签名中不存在同一地址的分离 space/index 参数；测试证明 Handler 交给 CommandBus 的是 `BusinessSlot`。 |
| AC-002 | RQ-004 | 成功与普通业务失败响应均由 `BusinessSlot` 反向映射形成 `GameSlot`，既有抓包样本的响应字节保持不变。 |
| AC-003 | RQ-005,RQ-006 | 对映射表每个区段的首尾地址执行双向往返均得到原值；未知、保留、虚拟和越界地址抛出专用异常且包含原坐标。 |
| AC-004 | RQ-007 | 映射异常被 Game 网络接收边界记录后仅终止当前异常请求/连接，Host 仍可处理后续独立连接；业务 Handler 不捕获为普通拒绝。 |
| AC-005 | RQ-008 | `Main,4 -> QuickSlot,1` 中的装备先通过快捷栏占位规则，再由行为规则决定能否分解；非装备得到行为拒绝，已存内容与空间规则冲突得到不变量失败。 |
| AC-006 | RQ-009 | 用既有数据库格式加载包含 Main、Equipment、仓库槽位的角色，业务读写后再次投影得到相同 GameSlot，且不要求破坏性数据库迁移。 |
| AC-007 | RQ-010 | 移动、使用、丢弃、出售、修理、分解及公共槽位规则测试通过；扫描没有残留私有偏移或旧签名，完整构建通过。 |

## Success Evidence

- EV-001：映射区段、往返、越界和专用异常单元测试，覆盖 AC-003。
- EV-002：六条协议 codec/handler/command 测试及现有样本回归，覆盖 AC-001、AC-002、AC-007。
- EV-003：快捷栏、Main 内移动、跨空间移动与分解槽位/内容组合测试，覆盖 AC-005、AC-007。
- EV-004：库存持久化加载、事务结算和状态投影测试，覆盖 AC-006。
- EV-005：网络异常隔离测试和日志断言，覆盖 AC-004。
- EV-006：完整差异扫描、解决方案构建和相关测试集的新鲜结果，覆盖全部验收条件。
