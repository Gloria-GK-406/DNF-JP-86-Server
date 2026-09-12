---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-7
  - SC-10
depends_on: []
---

# TaskContract: 可组合广播作用域与附近角色频道隔离

## Context

### Source Request

用户在 Normal 队长离队修正后要求优先处理附近可见性的频道边界：

> “周围人广播是按频道做区分的……不同频道是不能在同一个区域看得到的。先处理这两个问题吧。”
>
> “我们的广播可能要加一些可以组合的 flag 了。区域？频道？全服？比如有频道广播、全局广播、
> 区域队伍广播、频道区域在线人物的广播。”

### Current Situation

接受基线 `73a36166dfd2e6f2da85c8754a91ba724c2eab8f` 已完成 Normal 模式闭环。当前
`SessionPresence` 已保存实际 Game listener port、town、area、是否站在城镇和是否共享城镇空间，但
查询 API 只提供参数松散的 `InArea(..., listenerPort?)`。六个附近角色入口中只有会话清理显式传入
listener port；选择角色、相邻状态加载、跨区域及区域内移动会把相同 town/area 的其他频道角色纳入
名单或通知。

通知系统目前只有精确 `TheseCharacters` audience，不能直接表达“全服”“同频道”“同区域”及其组合。
公开队伍目录尚未实现，并且需要在后继结果中复用本规格建立的频道/区域约束。

## Goal

建立一个与消息主题无关、可组合且能确定性校验的在线会话/通知接收者作用域；把当前所有附近角色
进入、区域切换、移动和离开路径统一到“同实际频道 + 同共享城镇区域”的可见性规则，消除跨频道同
town/area 的角色泄漏，同时保持精确角色通知和同频道现有行为不回退。

## Scope

### In

- 为在线会话查询定义可组合维度，至少能表达全服、同频道、同城镇、同区域、实际站在城镇、共享城镇
  空间和排除某角色；非法或缺少锚点的组合必须在边界处拒绝。
- 让 presence/domain query 按任意合法组合筛选并稳定排序，保留 exact session generation 数据。
- 为通知 audience 增加基于该作用域的解析方式，同时保持 `TheseCharacters` 的顺序、重复项和当前会话
  解析语义不变。
- 把现有附近角色读取和广播路径全部接入同 listener/town/area/standing/shared 约束：首次进入、选择
  角色后的双方发布、跨区域旧/新区域、区域内位置变化和会话离开。
- 增加跨频道与同频道正反回归、合法/非法组合、解析时会话替换/离线及现有组队/通知回归；完成 Release
  构建、全量适用测试、独立完整结果审查和新鲜验证。

### Out

- 公开“查找队伍”目录的多队伍编码、创建/更新/移除 diff 与实际发布；它是复用本规格作用域的后继结果。
- 新增具体的全服公告、频道公告或其他业务消息；本规格交付表达与解析能力，不凭空增加消息类型。
- Story/Cycle 模式规则、P2P/UDP、副本战斗或奖励逻辑。
- 把队伍成员 generation-sensitive 通知从已接受的精确角色集合改成动态范围广播。

## Constraints

- 作用域只描述接收者约束，不嵌入“队伍”“角色”等消息主题；组合语义必须是交集，不能用互斥枚举把
  频道、城镇和区域绑死为单一模式。
- 频道身份使用当前选中会话的实际 Game listener port；同区域必须同时约束 town 与 area，不能仅比较
  一个可重用的 area 字节。
- 进入/移动等需要同一快照同时构造名单与 audience 的路径必须保持名单和接收者一致；不得因延迟解析
  产生消息体包含一组角色、实际发送给另一组角色的竞态。
- 保持 Application 不引用 Infrastructure；handler/Command/领域/通知职责边界与既有精确会话、
  generation/revision 保护不变。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 单一作用域模型可组合表达全服、同频道、同城镇、同区域和在线城镇可见性；非法组合或缺失锚点确定性失败。 | 值对象/查询正反测试，覆盖独立维度、交集、全服空约束、区域必须带 town 及缺失 listener/town/area。 |
| SC-2 | Presence query 对合法作用域只返回全部约束同时成立的记录，排除项生效并以稳定顺序返回；exact session 数据不被降级。 | 多频道、多 town/area、standing/shared、排除与排序的领域测试。 |
| SC-3 | 通知系统可直接解析范围 audience 到发送时仍在线的当前会话，同时保持 `TheseCharacters` 的既有顺序、重复和替代会话语义。 | audience resolver 正反测试、离线/替代会话测试及通知总线回归。 |
| SC-4 | 首次进入、双方角色发布、跨区域、区域内移动和会话离开均不读取或通知相同 town/area 的其他频道；同频道同区域行为保持不变。 | 每条生产路径的同频道正例和跨频道反例，名单内容与实际 audience 对照。 |
| SC-5 | 现有组队、城镇投影和通知交付无回归，且结果不声称已实现公开队伍目录或 Story/Cycle。 | Release build 0 warning/error、全量适用测试逐项 Passed、分层断言、独立完整结果审查和审查后 fresh verification。 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约记录的用户授权：要求“先处理”跨频道附近可见问题，并明确要求区域、频道、全服等广播可组合。 | 在根任务共享 worktree 内设计并实现通用作用域、presence/audience 解析、现有附近角色路径频道隔离、测试和文档；通过审查与新鲜验证后创建一个本地接受提交；失败时仅执行可证明的精确工作流回滚。 |

## Requires Confirmation

- merge、push、PR、发布、回滚已接受提交或清理根任务 worktree/分支不在本子规格授权内。

## Assumptions

- `ListenerPort` 是当前部署中频道/分线的可靠运行时身份；若后续证明多个业务频道共享同一监听端口，
  需要在根控制器中建立新的频道身份来源，而不能在本规格内猜测映射。
