---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: complete
---

# Implementation Plan: 响应式地下城入口可见性

## Planning Choice

使用 direct 详细计划：交付是一个共享实例变化到统一入口投影的连续重构，所有决策可在一份计划中维护，不需要独立交接、接受或回滚的 Task Unit。计划先固定领域变化，再迁移投影，最后改接两个协议入口，避免用 handler 补丁倒逼领域接口。

## Approach and Shared Decisions

- 保留 `DungeonRunDomainService` 作为共享实例仓库和角色反向索引；在同一锁内维护入口修订与已就绪参与者集合。
- 为入口准备和参与者就绪增加不可变领域变化结果。结果包含变化种类、`RunId`、修订、最新接收者专属视图和稳定 `CharacterId` 语义接收者，不携带 session 或网络消息。
- 保留现有 `Begin/BeginParty` 等兼容入口，新增的响应式入口方法成为生产入口路径；测试替身可通过接口默认实现维持轻量兼容，但生产实现必须覆盖原子语义。
- 新建统一 `DungeonEntryProjectionDelivery` 和按 `DungeonRunId` 串行化的投影 gate。投影获取 gate 后重新读取实例最新状态，并通过 `DungeonRunDeliveryRegistry` 与 selected-session directory 解析准确传输代际。
- 入口 wire 构造按接收者投影：共享首房间内容只解析一次语义来源；每份 `START_MAP` 使用该接收者的 participant view/slot；客户端创建成员/avatar 所需的选择前奏和用户状态先于 `START_MAP`。
- `FINISH_LOADING` 先通过 CMD 把当前调用角色的就绪提交到精确 run/revision，再由同一投影边界仅向该当前连接发加载完成直接通知，并保留实例变化供后续同步阶段复用。
- 城镇离开仍在完整入口投影发送成功后执行；入口准备或发送前映射失败时精确结束本次新 run，不删除 replacement connection 或较新的 run。

## Work and Dependencies

### T-001：用失败测试固定实例入口变化语义（RQ-001～RQ-003、RQ-009）

- 在 `Dfo.Server.Application.Tests/World/DungeonRunDomainServiceTests.cs` 增加双成员准备变化、接收者集合、入口修订单调、任意就绪顺序、重复提交、错误 run/修订、离开与新 run 替换用例。
- 先运行定向测试确认因缺少响应式入口接口而失败，再扩展 `IDungeonRunDomainService`、`DungeonRunState`/相关领域记录和 `DungeonRunDomainService`。
- 保证每次实例变化都在聚合锁内产生一致视图；领域程序集不新增对 Sessions/Networking/Protocol 的引用。

### T-002：建立统一入口投影与映射测试（依赖 T-001；RQ-004～RQ-006、RQ-009）

- 新增 `DungeonEntryProjectionGate` 与 `DungeonEntryProjectionDelivery`，职责包括：注册冻结参与者 generation、获取当前实例修订、剔除失效/换 run 成员、生成每个接收者的有序入口帧并一次性交付。
- 将目前 `SelectDungeonHandler` 中的 `ResolveAttachments`、逐成员运行态检查、选择前奏、`START_MAP` 个性化和扇出策略迁入统一边界；领域变化提供语义接收者，投影边界只负责传输解析。
- 在 Protocol 测试中捕获队长和成员的两份消息序列，断言成员集合、WireUserId、槽位、相同房间内容、不同 participantSlot、前置状态顺序、旧 generation 与旧 revision 抑制。
- 用投影 gate 保证同一 run 的延迟入口变化只重读并交付最新修订，不把旧快照画回客户端。

### T-003：把选择地下城改接实例变化路径（依赖 T-002；RQ-007～RQ-010）

- 精简 `SelectDungeonHandler`：保留请求上下文、任务/选图事实、队长授权和领域调用所需输入；不再自行枚举传输接收者、构造每成员帧或决定扇出。
- 让 `DungeonVisitDomainService.EnterPartyAsync`/入口协调结果携带实例准备变化与首房间描述，避免协议层从多个真源重新拼接参与者。
- 将 fatigue、投影失败回滚、delivery registry 和城镇离开边界接到统一入口结果，维持“先完整入口交付、后离开城镇”。
- 更新现有 `SelectDungeonHandlerTests`、`PartySelectDungeonHandlerTests` 和依赖构造器的测试，保持普通、剧情、单人及拒绝路径。

### T-004：把 FINISH_LOADING 改为角色对实例的就绪提交（依赖 T-001、T-002；RQ-002、RQ-005～RQ-007、RQ-009）

- 新增就绪 CMD/handler，将当前角色、当前 run 和当前入口修订提交给领域服务；无 run、旧 session、错误修订和重复提交不得改变实例。
- `FinishLoadingHandler` 不再是无状态发送器；它识别当前 selected session、调用 CMD，再经统一入口投影边界向提交者发送 `LoadingCompleteNotification`。
- 扩展 `FinishLoading` 测试覆盖两种成员就绪顺序、重复提交、replacement connection、非地下城地图兼容路径，以及加载完成通知仍只发给请求者。

### T-005：架构收口与回归（依赖 T-003、T-004；全部 RQ）

- 增加或扩展架构测试，禁止地下城领域引用 session/connection/协议消息，并证明两个入口 handler 没有成员扇出策略。
- 运行 Application 与 Protocol 定向测试，修复本次重构引起的兼容问题；随后运行完整解决方案测试与构建。
- 检查完整 diff，确认未修改换房、战斗、掉落、Boss、翻牌、数据库或客户端补丁。

## Verification

- 红灯证据：T-001、T-002、T-004 的首个行为测试在生产实现前因缺少预期行为失败。
- 定向绿灯：`Dfo.Server.Application.Tests` 中地下城领域测试，以及 `Dfo.Server.Protocol.Tests` 中 DungeonEntry、FinishLoading 和相关架构测试全部通过。
- 回归绿灯：`dotnet test Server/86JP.Server.slnx --no-restore` 的每个测试项目无失败；`dotnet build Server/86JP.Server.slnx --no-restore` 为零错误、零警告。
- 审查证据：完整 diff 能从每项 AC 追到领域测试、投影测试或 handler 集成测试；没有范围外行为变化。
- 交付声明仅为“达到双客户端实机验收条件”；合并部署后的互相可见实测不在本工作区内代替。
