---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: Character-scoped PartyService directory

## Planning Choice

采用 direct detailed plan。工作跨越数据契约、进程内 Party 实现、presence 生命周期和协议投递，
但所有步骤服务于同一个垂直切片，接口选择已经能够一次确定；不需要把细节拆成可独立交接或等待
前序探索结果的 Task Units。

## Approach and Shared Decisions

新增一个 Party 专用的不可变角色镜像，保存 AccountId、CharacterId、WireUserId 和可选的公共目录
scope。scope 只在角色实际站在共享城镇区域时存在，使用逻辑 ChannelId、TownId、AreaId 作为值键；
私有 `[gate]` 和非城镇状态用 `null` 表达，不创建伪实例键。

PartyService 不再读取 selected-session 或 presence 服务。协议命令处理器仍以当前连接为入口，在已有
transition gate 内从 selected session 与 presence 生成角色镜像，再调用 PartyService。角色 presence
的 Admit、Relocate 和 Withdraw 路径把新的可选 scope 推送给 PartyService，使其在同一内部锁下更新
队长目录归属及索引。

PartyNotificationPlan 把成员/邀请 audience 降为 CharacterId，并携带旧/新目录 scope 变化；协议
delivery 在领域外把 CharacterId 和 scope 解析为当前 SelectedSessionIdentity，再沿用现有精确投递、
版本门和重试。城镇初始投影直接以接收者 scope 调 PartyService.Query，不再从 represented sessions
逐个 FindParty 后去重。

## Work and Dependencies

### T-001 — 建立无 Session 的 Party 值契约（RQ-002–RQ-005、RQ-007）

- 在 Party 数据模型中加入 `PartyDirectoryScope`、`PartyDirectoryQuery`、
  `PartyCharacterSnapshot` 和目录变更值。
- 将 `PartyMemberSnapshot`、邀请/接受通知及 `PartyNotificationPlan` 的身份/audience 改为
  CharacterId/角色镜像；保留客户端编码需要的 WireUserId。
- 将 Party 服务接口收口为 `IPartyService`：所有命令使用角色镜像，查询使用 CharacterId 或
  `PartyDirectoryQuery`，并加入 presence 更新入口。
- 将成员状态读取接口改为角色镜像/CharacterId 输入，确保 PartyService 的依赖表面没有 Session。

依赖：无。先完成契约，后续编译错误作为完整迁移清单。

### T-002 — 重写 PartyService 内部状态与目录索引（RQ-001、RQ-004–RQ-007、RQ-009）

- 将 `PartyDomainService` 重命名/收口为 `PartyService`，移除 selected-session 与 presence 依赖，
  以 CharacterId 维护 membership、endpoint 和 invitation。
- PartyState 保存角色镜像成员及当前可选目录 scope；建立 scope 到 PartyId 的二级索引，并通过单一
  reindex helper 在创建、队长 scope 更新、未来队长替换和解散路径维护。
- 实现稳定排序的范围查询及 Category/ModeTarget 组合过滤；所有查询和操作结果在同一锁中构建
  脱离内部状态的快照。
- 保持邀请过期、队伍 generation/revision、容量、队长权限、成员槽位及解散规则不变。

依赖：T-001。

### T-003 — 把角色 presence 与逻辑频道接入 PartyService（RQ-003、RQ-005、RQ-006、RQ-008）

- 为 `SessionPresence` 增加入口已经解析的 ChannelId，同时保留 ListenerPort 供非 Party 的网络
  可见性投递使用。
- 在选角发布时从 `ChannelEntryPolicyState.ChannelId` 写入 presence；移动保留原 ChannelId。
- 让 presence 服务把 Admit/Relocate/Withdraw 后的公共 scope 变化通知 PartyService。无当前
  exact owner 的 stale withdraw 不得撤销 replacement presence 或队伍目录归属。
- 为现有 SessionPresenceScope 增加按逻辑频道查询共享区域的入口，仅供协议层解析 Party 观察者；
  不改变既有 ListenerPort 驱动的同屏投影。

依赖：T-001、T-002。

### T-004 — 迁移应用命令与 teardown（RQ-002、RQ-008、RQ-009）

- 在 Party command handler 中集中把 transition gate 已确认的 SelectedSessionIdentity 与其当前
  presence 转换为 PartyCharacterSnapshot；目标邀请同时转换双方镜像。
- teardown 在现有 replacement gate 和 exact presence 判定内按 CharacterId 清理 PartyService；
  PartyService 不再自行判断连接代际。
- 迁移成员 UI 状态读取，使其以角色镜像为请求并仅在适配器内部解析当前 runtime session。

依赖：T-002、T-003。

### T-005 — 迁移通知和城镇目录投影（RQ-008–RQ-010）

- 将 Party notification delivery 的 CharacterId audience 在发送前解析为当前 session，并保留现有
  exact-recipient 重试、generation/revision 流门和成功接收者不重放规则。
- 根据目录变更的旧/新 scope 查询当前角色 presence：旧范围发送 clear，新/当前范围发送 refresh，
  成员不作为公共目录观察者重复投递。
- 将 `PartyTownProjectionDelivery` 改为按接收者角色 scope 调 `PartyService.Query`，投影返回的全部
  队伍；represented sessions 只用于已有投影流同步，不再作为队伍发现源。
- 保持 Party codec 与消息顺序不变。

依赖：T-003、T-004。

### T-006 — 回归与架构保护（RQ-001–RQ-010）

- 先迁移现有 Application/Protocol Party 测试，再新增 PartyService 查询矩阵、相同数值私有 gate
  隔离、队长 scope 迁移、withdraw exact、防陈旧索引和可变载荷隔离测试。
- 新增架构断言，防止 Party 公共契约和实现重新依赖 SelectedSessionIdentity、ConnectionId 或
  ListenerPort。
- 运行 Party 定向测试、所有受影响测试、完整解决方案测试与构建；检查协议黄金字节未变化。

依赖：T-001–T-005。

## Verification

- 编译门：`dotnet build Server/86JP.Server.slnx` 成功且无 warning 降级。
- 定向行为门：Application Party、Session lifecycle、Protocol Party 和 TownProjection 测试全部通过。
- 全量门：解决方案内全部测试项目通过，单个 TRX 中每个 `UnitTestResult` 均为 Passed。
- 架构门：Party 边界禁止类型扫描/测试通过，Application 仍不引用 Infrastructure。
- 差异门：Party codec 黄金字节与既有通知顺序测试不变；本任务不把未进行的客户端实机复测写成
  新的 ClientVerified 证据。
