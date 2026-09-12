---
kind: rks-plan
spec:
  path: ./spec.md
  revision: 3
mode: task-units
state: ready
---

# Implementation Plan: A21 城镇组队闭环

## Approach

本计划使用 `task-units` 模式：组队领域、协议、通知、清理、资料库、HP 校准、角色详情和最终实机验收分别拥有可恢复、可独立复核的详细任务记录；`TASK-01` 至 `TASK-08` 是这些执行单元的唯一详细计划归属。

先在 `Dfo.Server.Data`、`Dfo.Server.Abstractions` 与 `Dfo.Server.Application` 建立不含网络对象的临时队伍模型和单一原子状态服务，再让 `Dfo.Server.Protocol/Game/Party` 通过四个 Command 接入端点上报、邀请、接受和离队。跨会话可见结果全部由类型化 Notification 投影，形成队伍时按四个独立发布阶段保持全体顺序。最后把精确会话清理接到同一队伍服务与投影计划，避免协议 handler、主动离队和掉线各写一套状态规则。

实施以测试驱动：先固定真实样本解码和领域不变量，再写通知字节及发布顺序，最后接生产注册与生命周期。扩围工作严格按用户指定顺序：先以失败的投递顺序测试固定“名册后补发 HP”并完成自动化回归，再实现 `GET_USERINFO mode=3` 当前会话详情投影；协议资料库随两轮实现同步，当前 A21 实机验证作为最后验收闸，不用自动化结果冒充客户端确认。

## Decisions Before Work

- **BD-001 状态边界**：新增 `IPartyDomainService` 作为队伍、邀请与成员端点的唯一写入口。接口只接收 `SelectedSessionIdentity`、wire uid 和协议无关值，返回不可变快照/投影计划；实现为 Application 单例，内部原子串行化，不依赖 Infrastructure。
- **BD-002 会话边界**：邀请和成员都保存完整 `SelectedSessionIdentity`。双人命令按 AccountId 稳定排序取得双方 `SelectedSessionTransitionGate` 排他租约，取得后再次校验当前目录与 presence，避免与选角/断线清理交叉提交。
- **BD-003 通知边界**：Application 返回 `PartyNotificationPlan`，Protocol 的共享投递器把它变成 `PartyInviteIssued`、`PartyRealtimeChanged`、`PartyInviteAccepted`、`PartyEndpointsChanged`、`PartyRosterChanged` 等 Notification。形成顺序由投递器统一拥有，handler 不直接拼跨会话帧。
- **BD-004 清理边界**：在 `SelectedSessionCleanupNotificationPlan` 增加可选队伍计划，而不把所有 town peer 与 party peer 合并。`SelectedSessionCleanupService` 在释放目录前调用队伍的精确离开操作；现有 `CleanupNotificationDelivery` 仍先完成原有 owner/town 通知，再调用共享队伍投递器。
- **DP-001 队长退出**：任务不实现转移队长，因此队长主动离开或掉线时解散全队；普通成员离开只刷新幸存队伍。
- **DP-002 缺失端点（实机修订）**：首轮 A21 实测证明零地址/零端口并非客户端安全的“未知”表达；按同版本旧服用 `127.0.0.1:10000`、NAT 0、MTU 1500 回落。收到有效上报后仍使用真实值刷新后续投影。
- **DP-004 私有 gate**：PVF `[gate]` 是每个城镇的赛丽亚到达房间，不是共享大厅。entry/movement 将该内容事实带入 presence，所有 `InArea` 消费者统一排除私有记录；`[normal]` 与 `[dungeon gate]` 不变。
- **DP-003 邀请时间**：使用 `TimeProvider` 和一个明确的短期有效窗口；具体时长集中为单一常量，测试只验证边界与过期性质，不把 UI 推测写进协议事实。
- **DP-005 HP 消费顺序（实机修订）**：保留已经证明可形成队伍的 `0x0099 -> 0x0008 -> 0x000B -> 0x0009`，在完整名册之后从同一快照向全体再投影一次 `0x0099`。不等待或伪造客户端 `0x00A6`，也不把补发写成新的领域行为。
- **BD-005 详情安全边界**：`GET_USERINFO mode=3` 经独立 Command 查询当前在线目录；目标必须在请求者的同一 listener 中唯一解析。Command 只返回带请求者与目标精确代际的不可变详情计划，Notification 投影发送前再次校验双方当前性，避免跨频道、离线或旧代际泄露。
- **DP-006 详情 wire 形状**：只采用 A21 客户端解析器、同版本参照实现和逐字节测试共同支持的最小详情响应；未知字段维持可追溯的常量或现有生产投影，不借详情查询扩展装备评分、社交或其他范围外行为。

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-003,RQ-005,RQ-006,RQ-009,RQ-010,RQ-011 | 在 `Server/src/Dfo.Server.Data/Parties` 定义成员、端点、快照、拒绝原因和通知计划；在 `Abstractions/Parties` 定义领域接口；在 `Application/Parties` 实现原子邀请、接受、离开、端点更新和精确会话清理，并注册单例。 | `Dfo.Server.Application.Tests/Parties` 先失败后通过，覆盖自邀、域不一致、非队长、满员、一次性/过期/换代邀请、slot、普通成员退出、队长解散和旧会话清理。 |
| T-002 | RQ-001,RQ-002,RQ-004,RQ-005,RQ-009,RQ-011 | 新建 `Protocol/Game/Party`：严格 decoder、四个 Command/handler 和消息 handler；把 `0x0002` 从只记录的 map-entry handler 拆出并交给 Command；为 `ClientMessageType`、协议常量与 DI 注册补齐入口。 | `Dfo.Server.Protocol.Tests/Game/Party` 验证两个 31B 样本、畸形端点、`0x000A` 7/9B 形态、范围外 type 无状态副作用、`0x000B` 有效/无效响应和 `0x000D` 空 body。启动注册校验通过。 |
| T-003 | RQ-004,RQ-007,RQ-008,RQ-011 | 实现五种 NOTI 的类型化消息、编码器、Notification handler 和共享 `PartyNotificationDelivery`；从一个快照生成 `0x0099 -> 0x0008 -> 0x000B -> 0x0009`，并提供清队名册形态。 | 字节级测试固定 `0x0007/0008/0009/000B/0099` 长度、端口序与 slot；投递测试证明全体阶段顺序、接受帧只给邀请者、清队 audience 与幸存者刷新互不串线。 |
| T-004 | RQ-009,RQ-010,RQ-011 | 将队伍精确清理接入 `SelectedSessionCleanupService`、返回选人、账号替代和网络断开共用的既有清理计划；扩展清理投递器调用 T-003 的共享投影。 | 扩展 Application/Protocol 生命周期测试，证明旧代际不会移除新状态，普通成员掉线刷新，队长掉线解散，并保持原有 owner-before-town-peer 顺序。 |
| T-005 | RQ-012 | 将本次真实 C2S body 归档到协议页相邻 samples；更新 `0x0002/000A/000B/000D`、`NOTI 0x0007/0008/0009/000B/0099` 的人工区、实现映射与待验证项；为 `0x01A3/0122` 和 `0x000A type=1` 记录本次已观察但范围外；同步 `protocol/game/index.md` 与 `protocol/catalog.md`。 | 文档链接和样本存在；搜索不再显示这些实现为“尚无观察记录”或错误的无实现状态；任何未经过当前实机的项保持 `Implemented`/待实机验证。 |
| T-006 | RQ-001..RQ-014 | 收口既有组队自动化与实机校准，并按 Round 5 证据先修复 HP：用顺序测试固定完整名册后的第二次实时摘要，复用同一快照投影到全体成员，同步 `NOTI 0x0099`/形成顺序文档。 | 定向测试先证明旧顺序缺少名册后摘要，再证明双方均按 `... -> 0x0009 -> 0x0099` 接收相同快照；组队全量回归、构建和逐项 TRX 通过。本单元只完成 HP 实现与自动化子结果，最终 UI 闸移至 T-008。 |
| T-007 | RQ-011,RQ-012,RQ-015 | 独立实现 `GET_USERINFO mode=3`：分流三字节请求，建立同 listener 唯一当前目标查询 Command、双代际详情计划与发送前复核；按 A21 解析/同版本证据编码最小详情响应并同步 C2S/S2C 文档。 | RED/GREEN 覆盖 mode 2 无回归、mode 3 成功、离线/跨 listener/歧义 uid/双方换代拒绝、逐字节响应向量和生产 DI；Application 仍不引用 Infrastructure。 |
| T-008 | RQ-001..RQ-015 | 运行新鲜全量构建与全量测试并逐项检查 TRX；启动任务 worktree 的 Release Host，用当前 A21 双客户端在共享区域完成邀请、接受、双端初始 HP、角色详情和普通成员离队；归档服务端关联日志与 UI 观察，完成资料库、结果审查和最终证据。 | 自动化无失败/跳过；双端 UI 和服务端顺序满足 AC-009、AC-012、AC-013。若实机尚未执行，结果保持“实现就绪、等待实机”，不得完成 Spec 或提交接受结果。 |

## Dependency Order

`T-001` 的不可变快照和投影计划是 `T-002/T-003/T-004` 的共享契约；`T-002` 与 `T-003` 在其后可并行局部实现，但生产 handler 只有两者都完成才接通；`T-004` 复用同一离开行为与通知投递；`T-005` 等真实字段和实现位置稳定后进行；`T-006` 先完成用户指定的 HP 修复与自动化门，`T-007` 才开始角色详情；`T-008` 是完整结果闸。

## Choices During Implementation

- `RESPONSE_PEER` 的可接受总长度按静态构包器与旧服同版本证据取最小兼容集合；当前 A21 首次接受/拒绝抓包如给出不同形态，先增加样本和测试，再扩展 decoder，不按猜测吞任意尾部。
- 队伍名册清除优先采用旧服同版本已用于离队的 `type=3` 定向清除形态；角色入场的 `00 00` 保持“初始无队基线”。实机若显示残留，以捕获到的客户端解析行为校准并同步文档。
- 现有 `MapEntryReportHandler` 继续处理其他纯上报；只把具有新增领域效果的 `SET_UDP_IP_PORT` 拆出，避免给整个上报族引入 Command 依赖。
