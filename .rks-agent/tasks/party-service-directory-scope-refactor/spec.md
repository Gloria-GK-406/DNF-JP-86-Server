---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Character-scoped PartyService directory

## Why and Outcome

队伍领域需要从网络连接生命周期中独立出来。完成后，PartyService 是进程内队伍行为、状态和
目录索引的唯一边界；它只认识角色身份、角色位置镜像和队伍值对象，不认识 Session、连接代际、
ListenerPort 或可发送连接。应用/协议层负责确认请求来自当前连接、把连接映射为角色镜像，并把
PartyService 返回的不可变结果投递给当前会话。

公共队伍目录以队长当前所在的“逻辑频道 + 共享可见区域”为归属。查询者只能得到同一归属中
满足过滤条件的队伍镜像；私有 `[gate]` 不形成可被其他角色查询的公共目录。该结果保持现有 A21
客户端通过 CNUser 与 `0x0009` 组合生成队伍行的协议行为。

## Definitions

- **Character presence snapshot**：角色领域在某一时刻给出的不可变在线位置视图，至少包含
  CharacterId、逻辑 ChannelId、TownId、AreaId、是否实际站在城镇以及是否共享城镇可见性。
- **Directory scope**：可作为稳定相等键的逻辑频道与共享可见区域组合。它必须同时约束 TownId
  和 AreaId，并显式表达“可公共共享”；私有或非城镇 presence 不产生公共 scope。
- **Party snapshot**：从 PartyService 内部状态一次性复制出的队伍版本，包含 PartyId、版本、
  队长角色/网络用户身份、设置、标题及成员值。它不暴露内部集合或任何 Session 对象。
- **Current-connection gate**：应用/协议层在调用 PartyService 前确认请求会话仍是该角色当前连接；
  旧连接 teardown 也必须在进入角色领域前完成或被挡住。

## Scope

包括 Party 公共契约、数据模型、进程内实现、应用命令适配、城镇目录投影、队伍通知投递及相关
自动化测试。保留现有 A21 消息布局、客户端资源、Session 生命周期实现和进程内存储方式。

不扩大目录到整座城镇/整个频道，不实现跨进程队伍，不修复剧情地下城进入流程，也不修改
debug-lab。若剧情地下城随后把角色 presence 更新为非城镇状态，本规格提供的 PartyService 边界
必须能够正确撤销其目录归属。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | PartyService 必须成为队伍、邀请、成员端点及目录索引的唯一进程内写入者；服务内部可以有私有可变记录，但不得公开行为型 PartyInstance。 |
| RQ-002 | PartyService 的公开命令、查询、结果、成员和通知计划只能携带角色身份、角色镜像及队伍值对象，不得携带 SelectedSessionIdentity、NetworkConnectionId、连接代际、ListenerPort 或网络会话。 |
| RQ-003 | 角色位置镜像必须使用逻辑 ChannelId；网络入口可以保留 ListenerPort，但必须在调用 PartyService 前完成 ListenerPort 到 ChannelId 的解析。 |
| RQ-004 | PartyService 必须提供按 Directory scope 查询全部 PartySnapshot 的接口，并支持按现有 PartyCategory 与 ModeTarget 组合过滤；空结果用空集合表达。 |
| RQ-005 | 可公开查询的 Directory scope 必须是同 ChannelId、TownId、AreaId 且共享城镇可见；私有 `[gate]`、未完成城镇入场或已进入地下城的角色不能成为公共目录锚点。 |
| RQ-006 | 一个队伍的目录归属由其当前队长的最新角色位置决定，并在建队、队长位置变化、离开/进入公共可见域、队长变化和解散时与队伍状态原子一致。 |
| RQ-007 | PartyService 对外返回的每个 PartySnapshot 都必须与内部可变状态脱离；修改返回的数组、标题字节或其它可变载荷不得改变之后的查询结果。 |
| RQ-008 | 协议/应用层必须在 PartyService 外执行 current-connection gate、角色镜像解析、观察者会话解析和网络投递；旧连接不得通过迟到命令或 teardown 修改 replacement character 的队伍状态。 |
| RQ-009 | 现有建队/设置与改名、邀请/接受、加入/离队、解散、队长变化、端点和队伍实时查询语义以及 A21 封包字节布局必须保持兼容。 |
| RQ-010 | 城镇初始投影和队伍状态变更投影必须从 PartyService 的范围查询/变更结果产生；不得通过遍历 Session 再反向推断有哪些队伍。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001, RQ-002 | 静态架构测试检查 Party Abstractions/Data/Application 的公开队伍边界和内部成员记录，不存在被禁止的 Session/Connection/Listener 类型；PartyService 仍是唯一注册实现。 |
| AC-002 | RQ-003, RQ-004 | 给定多个队伍分别位于不同 Channel、Town、Area、Category 和 ModeTarget，使用一个查询可以返回所有且仅返回完全匹配的 PartySnapshot；任意过滤项为空时只放宽该项，不改变其它范围约束。 |
| AC-003 | RQ-005 | 给定两个具有相同 Channel/Town/Area 数值但 `SharesTownPresence=false` 的 `[gate]` 角色，二者不能查询到对方的公共队伍；给定共享 `[normal]` 或 `[dungeon gate]` presence，则同 scope 查询可以看到对应队伍。 |
| AC-004 | RQ-006 | 建队后只出现在队长当前 scope；队长迁往另一共享 scope 时旧 scope 立即不再返回、新 scope 开始返回；队长转移后归属切换到新队长 scope；队长进入非城镇状态或队伍解散后所有公共查询均不再返回该队伍。 |
| AC-005 | RQ-007 | 取得镜像后篡改调用方持有的可变载荷，再次读取同一队伍所得标题、设置、成员和索引均保持原值；并发读写不会返回成员版本和目录版本不一致的混合结果。 |
| AC-006 | RQ-008 | 用旧连接与 replacement connection 构造协议命令/teardown 场景：旧连接在应用层被拒绝或等待清理，PartyService 只收到 CharacterId/角色镜像；当前连接仍能收到正确通知。 |
| AC-007 | RQ-009 | 既有 Party Application/Protocol 测试迁移后全部通过，消息 codec 的黄金字节不变，建队、改名、解散及刷新消行所依赖的通知顺序没有变化。 |
| AC-008 | RQ-010 | 城镇投影面对一个接收者时，以其角色位置镜像调用 PartyService 范围查询并投影返回集合；测试证明实现没有通过 Session 列表发现队伍，且多队伍结果均可投影。 |
| AC-009 | RQ-001–RQ-010 | 解决方案完整构建、相关测试和 Application 不引用 Infrastructure 的架构测试全部通过，不以禁用测试、放宽 warning 或修改协议基线绕过失败。 |

## Success Evidence

- EV-001（AC-001）：Party 边界架构测试及对禁止类型的源码检查结果。
- EV-002（AC-002–AC-005）：PartyService 范围查询、迁移、私有区域与快照隔离单元测试。
- EV-003（AC-006–AC-008）：应用命令、通知投递和城镇投影协议测试。
- EV-004（AC-007）：现有 A21 Party codec/通知黄金测试结果；实机复测不可用时明确保留为待复测，
  不提升 ClientVerified 状态。
- EV-005（AC-009）：全量解决方案构建及测试结果。
