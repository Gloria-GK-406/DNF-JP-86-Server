---
kind: rks-task-contract
revision: 2
state: ready
---

# TaskContract: A21 公共队伍目录完整闭环

## Context

### Source Request

旧 `party-mode-rules` 的公共目录 Spec 因把 `S2C NOTI 0x023C` 视为三标签主目录数据源、又缺少真实
非空 record 与原生 UI 可见性而封存。用户要求继续深入，先解决全部 blocker：

> “你看看能不能继续做,如果不能就让另一个agent继续深入收集.把所有阻塞项都解决了再说.”

本任务是独立后继，不重开或改写旧终态。

### Current Situation

接受基线 `9a3a0b492da11f48525d1225d02513e0dd1cd458` 已具备 byte-exact `PartySettings`、严格 C2S
`0x000C`、完整 `NOTI 0x0009` party snapshot、队伍 lifecycle、revision/generation 与 town presence。
固定当前 B1 的独立复核已纠正旧报告：

- ID `0x11` `IntegratePartyListWindow` 主目录的行生成/渲染只依赖 `NOTI 0x0002` 创建的 `CNUser` 和
  `NOTI 0x0009` 维护并关联的 party object；远端 leader 的 `0x0002` 必须先于观察者收到其 `0x0009`。
- `0x023C` manager 的逐行消费者属于 ID `0x14` `RegistWaitingListWindow` 候补窗；ID `0x11` 只在
  `0xF749xx` 查询当前队伍是否已报名，不用它生成或渲染主目录行。
- matching-info 来自客户端本地 `Etc/PartyMatchingInfo.etc`，不是服务端协议前置。
- 当前服务端已经有正确 town-visible `0x0002` 与合法 `0x0009`，缺口是非成员观察者 audience、
  entry/move/mutation/cleanup 顺序与 exact-session 收敛。

## Goal

交付 A21 ID `0x11` 三标签主目录的端到端功能：同一 town-visible scope 的非成员可从正常
`0x0002 -> 0x0009` 投影看到、更新并移除队伍；复用客户端派生视图，不新增无消费者的目录协议或仓储。

## Scope

### In

- 固定两个窗口的真实对象/协议边界并封存旧误判；证据均绑定当前 B1/hash。
- 复用现有 `0x0002` town presence 和 `0x0009` roster/settings/clear，向准确的 town-visible 非成员
  exact sessions 补投影；成员现有四阶段通知保持不变。
- 首次选角、换 area/town、创建/编辑、accept、普通成员离队、leader 解散、断线/重选均满足
  `0x0002 -> 0x0009` 和 clear-before-observer-refresh 顺序。
- 同一 party 的 entry read-repair 与 mutation 共用 generation/revision gate；新观察者即使 revision 已提交
  仍能收到当前 snapshot，旧 exact session 不命中新连接。
- 满员队保留并由客户端置灰，不把它错误移出主目录；不同 town/area/channel 不泄漏。
- 当前固定 A21 的原生 UI 添加/刷新/删除验收；同步协议记录、样本、trace、index/catalog 和证据等级。
- 自动化覆盖因果模型、顺序、去重、失败重试、旧 revision/generation、精确会话与生命周期组合。

### Out

- `0x01BC/0x01BD/0x01BE` 快速组队队列及 `0x017F/0x0180/0x0181`；静态报告已证明其 UI、manager
  与目录独立。
- ID `0x14` 候补窗的 `0x0283 -> 0x023C`、真实非空 record、分页与筛选；matching-info 是已闭合的
  ID `0x11` 本地筛选配置，不归入此排除项。
- C2S `0x0183`、Story/Cycle 模式 target 规则、地下城进入状态机、PVP C2S `0x023C`、窗口 ID `0x183`。
- 新 `0x023C` codec/handler、目录仓储、全局/频道 party 枚举 API 或 per-channel directory gate。
- merge、push、PR、发布、旧 blocked Spec 改写，或旧/current worktree 清理。

## Constraints

- 所有地址必须绑定当前 B1 SHA-256 与 ImageBase，静态/运行时/实现证据分级；当前客户端归因前不得写
  `ClientVerified`。
- audience 必须与基线 `VisibleTownArea` 的 `0x0002` CNUser 可见域一致，不能扩大为整个 channel。
- 相关远端 CNUser 的 `0x0002` 必须先于观察者的 `0x0009`；反序修复必须重发 `0x0009`。
- 成员与非成员受众必须分区，避免 acceptance/roster 重复；离队者仍在 scope 时先 clear 再作为 observer refresh。
- 所有 audience 保留 `SelectedSessionIdentity`，旧计划不得投递给 replacement session。
- ID `0x11` 不得新增 `0x023C` 生产路径；`0xF749xx` 的报名状态查询只记录为伴随状态，不冒充行数据源。
- Application 不得引用 Infrastructure；handler 调 Command 实施业务并通过 Event/Notification 通知，
  Command 使用领域实例和领域/规则服务。
- 不使用 Computer Use 操作游戏窗口；需要原生 UI 动作时只提出一次明确用户操作，并以进程/帧/日志/
  调试器证据观测。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | ID `0x11`/`0x14`、matching-info、`0x023C`/`0x0183` 的归属已裁决，主目录不再依赖错误 blocker。 | 固定 B1 factory/vptr/producer/consumer 调用链、报告冲突裁决与独立审查。 |
| SC-2 | 服务端以现有 `0x0002/0x0009` 为 town-visible 非成员建立、刷新和清除 party 派生视图；不新增 `0x023C` 目录协议。 | golden codec 回归、client-state 因果模型、入口/换区/party mutation/cleanup 行为测试。 |
| SC-3 | 初始、创建、编辑、满员、accept、离队、解散、断线和换区按 exact session 正确收敛，且硬顺序正确。 | Command/领域/事件/通知测试、旧 revision/generation、失败重试、replacement 与跨 scope 组合回归。 |
| SC-4 | 当前固定 A21 原生 ID `0x11` 窗口可见至少一条合法非成员队伍，并能观察更新与删除，无需客户端逻辑绕过。 | exact session 的 `0x0002/0x0009`、Host、CNUser/party pointer、`this+0x188` 与 UI add/update/delete 归因。 |
| SC-5 | 既有 Normal/组队生命周期/频道隔离无回退，协议资料与实现一致且没有误接快速组队/PVP。 | 聚焦与全量测试逐项 Passed、Release build 0 warning/error、分层断言、协议一致性、完整独立审查和审查后验证。 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 用户明确要求判断能否继续、不能则派 agent 深入收集，并“把所有阻塞项都解决了再说”。 | 从接受基线建立独立工作树；并行只读逆向/证据收集；实现、测试、协议/任务文档、定向本地运行验证、每个独立验收结果的一个本地提交，以及未验收尝试的精确工作流回滚。 |

## Requires Confirmation

- 需要用户执行当前 A21 原生 UI 动作时，说明唯一动作、预期观测与停止条件后等待；不使用 UI 自动化代替。
- merge、push、PR、发布、已接受结果回滚、外部参考服务写入或 worktree/分支清理需要新的明确授权。

## Assumptions

- 接受基线 `9a3a0b49` 的 party snapshot、通知队列和频道作用域可作为目录投影基础；若审计发现其无法
  表达已证目录语义，则以独立子规格处理，不修改已接受历史。
- `Document/A21-party-directory-and-quick-match-static-contract.md` 的 ID `0x11`/`0x14` 混合结论已由三份
  follow-up 和纠正版服务端审计替代；原报告仍作为冲突来源保留，不作为生产合同。
