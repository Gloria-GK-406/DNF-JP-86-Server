---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: PartyService 与队伍目录范围重构

## Context

### Source Request

> 对，然后组队服务就要用character的镜像或者instance来查询。用channel，区域等关键字来查询。然后我看队伍本身的行为应该不多，直接改成用partyService来承载，而不是再建立一个partyInstance可能会更好。service这次只返回镜像。这里的区域要看看队伍分割的范围是多大的。如果是按城镇来分，那就是区域就是城镇的。如果是区域，那就是区域。你先看看给我一个结论
>
> 可以，就按这个先开始一个重构任务契约

### Current Situation

当前 `PartyDomainService` 已经是进程内队伍状态的唯一写入者，并在内部保存私有
`PartyState`，但其公开命令、成员快照和通知 audience 仍携带
`SelectedSessionIdentity`；队伍目录观察者也通过 `ISessionPresenceDomainService`
按 `ListenerPort + TownId + AreaId` 临时扫描得到。这样把连接代际、监听端口、角色位置和
队伍领域混在同一契约中，也没有提供一个按逻辑频道及可见区域直接返回全部队伍镜像的
独立查询边界。

A21 客户端主组队窗口不会自行比较 town/area，而是枚举已经物化的 `CNUser`，再通过
`0x0009` 关联队伍。当前静态证据、服务端实现和双客户端实机证据共同支持的公开目录边界是
“同逻辑频道、同一共享可见区域”：`[normal]` 与 `[dungeon gate]` 可共享，`[gate]`
为每角色私有；相同的 TownId/AreaId 不能单独证明两个角色处于同一可见域。现有证据不支持
把目录扩大为整个城镇或整个频道。

## Goal

建立一个独立、进程内的 `PartyService` 作为队伍状态、行为和查询的唯一领域边界：它以角色
领域提供的不可变在线位置镜像为输入，按逻辑频道和共享可见区域过滤队伍，对外只返回脱离
内部可变状态的队伍镜像；连接、Session、监听端口和连接代际留在登录生命周期及协议投递层。

## Scope

### In

- 将建队、设置/改名、邀请、加入、离队、解散、队长变更及成员端点等现有队伍行为统一收口到
  独立 `PartyService`；允许服务内部保留私有 `PartyState`/记录，但不增加公开的
  `PartyInstance`。
- 让 Party 公开命令和查询使用 CharacterId 及角色领域拥有的不可变在线位置镜像，不再接收、
  保存或返回 `SelectedSessionIdentity`、ConnectionId、连接代际或 ListenerPort。
- 建立按 `ChannelId + 共享可见区域` 查询全部匹配队伍镜像的契约，并容纳现有队伍类别、目标等
  可组合过滤条件；查询结果来自 PartyService 自有注册表/索引，而不是扫描 Session。
- 定义足以区分共享 `[normal]`/`[dungeon gate]` 与私有 `[gate]` 的可见区域身份；不得只用裸
  `AreaId`，也不得把私有 `[gate]` 中数值相同的角色合并进公共目录。
- 保证目录归属跟随当前队长的角色位置：建队、队长移动、进出公共可见域、队长变更和解散后，
  过滤查询不得留下旧范围中的陈旧条目。
- 调整应用命令及协议通知边界，使其在 PartyService 外解析当前连接并投递现有 A21 队伍消息，
  保持已经实机验证的建队、改名、解散和刷新消行行为。
- 为队伍行为、范围过滤、私有区域隔离、快照隔离和范围迁移补充自动化回归，并保持解决方案构建
  与分层约束通过。

### Out

- 删除 Session 子系统，或改变登录/重连 teardown 的并发门控方案。
- 修改 A21 客户端、ClientHack、协议 opcode、既有封包布局或客户端本地
  `PartyMatchingInfo.etc` 筛选逻辑。
- 在缺少新实机证据时，把公共队伍目录扩大为同城镇、整个频道或跨频道范围。
- 修复剧情地下城进入流程本身；本任务只提供并接好队长离开/进入可见域时所需的队伍目录更新
  边界。
- 将进程内队伍状态持久化到数据库，或引入跨进程分布式队伍目录。
- 修改或清理 `debug-lab`。

## Constraints

- `PartyService` 是队伍状态及其查询索引的唯一写入者；对外结果必须是不可变、脱离内部集合的
  `PartySnapshot`，调用者不能取得或修改内部 Party 记录。
- 频道过滤使用逻辑 `ChannelId`；ListenerPort 只能由网络/入口层用于解析 Channel，不得继续作为
  Party 领域中的频道身份。
- 区域过滤使用角色领域给出的共享可见域语义。`TownId + AreaId` 可以参与构成身份，但不能绕过
  `[gate]` 私有隔离规则。
- Session 代际校验、旧连接 teardown 和 replacement connection 防误删由登录生命周期在调用
  PartyService 之前解决，不得通过把 Session 身份重新写入 Party 状态来规避。
- PartyService 的行为结果与查询镜像必须来自同一次受保护的状态变更，避免成员关系已经变化而
  目录索引或返回镜像仍属于旧版本。
- 保持 Application 不引用 Infrastructure 的既有分层规则。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Party 公开服务契约、内部成员记录及公开镜像不再包含 Session、ConnectionId、连接代际或 ListenerPort；协议投递仍能在领域外找到当前连接 | 代码检查与架构测试证明 Party 边界只使用角色身份/镜像；对应协议处理测试成功投递到当前连接 |
| SC-2 | 调用方可以用逻辑 ChannelId、共享可见区域及可选队伍条件一次取得全部匹配的不可变 `PartySnapshot` | 查询契约及测试覆盖零条、一条、多条、跨频道、跨区域和组合过滤结果 |
| SC-3 | 相同 TownId/AreaId 的私有 `[gate]` 角色不会互相取得公共队伍条目；共享 `[normal]` 与 `[dungeon gate]` 仍按同一可见区域返回 | 自动化测试使用相同数值区域但不同共享语义验证隔离，并覆盖两类共享区域 |
| SC-4 | 队长移动、离开城镇可见域、进入地下城、队长变更或队伍解散后，旧查询范围不再返回陈旧队伍；进入新共享范围后仅新范围可见 | 状态迁移测试逐步查询变更前后的两个范围，并证明索引与 Party 当前版本一致 |
| SC-5 | 建队、改名、邀请/加入、离队、解散及队长变化继续满足既有业务不变量，外部修改返回镜像不能改变服务内部状态 | 现有队伍测试迁移通过，并增加快照隔离及容量/成员/队长一致性测试 |
| SC-6 | 已实机确认的“创建后出现、改名后刷新、解散先取消组队并在刷新后消行”协议行为没有因重构改变 | 协议编码/通知回归通过；在可运行客户端环境中保留同范围双客户端复测记录，若当轮无法实机则明确标记为待复测而不冒充 ClientVerified |
| SC-7 | 重构没有破坏解决方案构建、测试和 Application/Infrastructure 分层边界 | 全量构建、相关测试项目及架构约束测试均通过，无警告降级或跳过失败 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 用户在接受上述 PartyService、角色镜像及同频道共享可见区域结论后明确表示：“可以，就按这个先开始一个重构任务契约” | 建立并推进本契约所界定的 PartyService 与队伍目录范围重构；不包含合并、推送、工作区清理、客户端修改、debug-lab 或剧情地下城功能修复 |

## Requires Confirmation

- 将完成结果合并到其他分支、推送远端或清理任务工作区。
