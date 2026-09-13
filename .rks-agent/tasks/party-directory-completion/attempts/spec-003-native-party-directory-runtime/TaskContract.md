---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
- SC-4
- SC-5
depends_on: []
---

# TaskContract: 固定 A21 原生公共队伍目录运行时闭环

## Context

### Source Request

用户要求继续处理并在所有阻塞项解决后再报告：

> “你看看能不能继续做,如果不能就让另一个agent继续深入收集.把所有阻塞项都解决了再说.”

### Current Situation

接受基线 `365baed09ee5a19c08550a4f5dbdaecdafca7c8f` 已完成静态数据源裁决和服务端
town-visible nonmember `0x0002 -> 0x0009` 投影，并经独立审查与全量回归接受。根任务只剩固定当前 A21
原生 ID `0x11` 窗口的 add/update/delete 实机证据以及累计收口；自动化不能证明客户端 UI 真正消费、关联和
移除这些对象。

## Goal

在不绕过客户端逻辑的前提下，以两个当前 A21 exact sessions 证明同一 town-visible scope 的非成员可在原生
ID `0x11` 主目录看到一条合法队伍，随后观察其更新与删除，并把 wire、Host、CNUser/party association、
窗口状态和可见 UI 结果归因到同一条时间线。若暴露本任务范围内的投影缺陷，完成最小修复并重新验证。

## Scope

### In

- 准备可复现的本地 Host、两个客户端会话、日志/抓帧和只读进程/调试观测；固定 executable/hash、配置、
  account/character、listener/channel/town/area 和 session generation。
- 让一个角色作为队长创建普通队伍，另一名同 scope 非成员打开原生 ID `0x11` 三标签主目录；验证新增、设置
  或成员变化后的刷新，以及解散或离开可见域后的删除。
- 对 observer 精确关联其入站 `0x0002` 与后续 `0x0009`，并在客户端侧确认 CNUser、party association、
  `this+0x188`/等价目录状态与原生行渲染，而非只凭服务端发送成功推断 UI。
- 运行期间发现的同一投影/顺序/exact-session 小缺陷可做聚焦修复、自动化防回归、协议证据更新和重新实测。
- 同步 runtime evidence、协议置信度、根控制器输入，并完成独立审查与审查后验证。

### Out

- ID `0x14` 候补窗、`0x0283 -> 0x023C`、快速组队、Story/Cycle、地下城进入、PVP 或新目录协议/仓储。
- 客户端逻辑绕过、伪造 UI 成功、手工编辑 exe/dll、修改只读 ClientHackLab 基准目录。
- merge、push、PR、发布、接受结果回滚或 worktree/分支清理。

## Constraints

- 运行时证据必须绑定当前 A21/B1 SHA-256、ImageBase 和 exact sessions；失败、未观察或只观察到 wire 时不得
  升级成 `ClientVerified`。
- 不使用 Computer Use 操作或读取游戏窗口；原生 UI 动作由用户完成，只提出一次明确动作请求，并说明预期
  观测和停止条件。其余观测使用进程、窗口元数据、文件、帧日志和调试器/只读内存证据。
- `0x0002` 必须先于相关 `0x0009`；受众不得超出 `VisibleTownArea`，成员/observer 和 replacement session
  必须保持分区与精确性。
- 不新增目录意义 `0x023C`，不把 ID `0x14` 或快速匹配链误接到 ID `0x11`。
- 客户端目录中的二进制只从已验证构建产物部署；所有配置或部署变更须可逆、保留基线身份并在结果中记录。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Host 与两个当前 A21 会话可复现，身份、scope、抓帧和客户端状态观测同时可用。 | executable/plugin/config hashes、Host readiness、两个 exact session 身份及日志/探针起止边界。 |
| SC-2 | observer 在 leader 的 `0x0002` 后收到合法 `0x0009`，原生 ID `0x11` 新增一条对应非成员队伍行。 | exact-session frame/Host 时间线，加上 CNUser/party association、目录状态与用户原生 UI 观察的同一对象归因。 |
| SC-3 | 设置或成员变化使同一行刷新；解散或离开可见域使该行删除且无幽灵/重复。 | update/delete wire、客户端对象/目录状态和用户 UI 观察的前后快照；失败时聚焦复现与修复后重验。 |
| SC-4 | 不同 scope 和 replacement 不泄漏，满员队仍存在但由客户端置灰；既有成员流程不回退。 | 聚焦 runtime/自动化证据与 exact-session/scope 对照；无法稳定操作的附加场景不得冒充已验证。 |
| SC-5 | runtime 结论与协议资料、实现和自动化一致，完整结果独立审查通过并有审查后新鲜验证。 | evidence manifest、协议记录/catalog、必要回归、Release build、独立 review 和 completion evidence。 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 用户明确要求继续并“把所有阻塞项都解决了再说”；根 TaskContract revision 2 已将原生 UI 验收和定向本地运行验证纳入授权。 | 在共享 task worktree 与明确客户端/Host 测试面内准备和执行双会话验证，采集只读证据，做必要的范围内投影修复与测试/文档，并形成一个本地接受提交。 |

## Requires Confirmation

- 环境和探针准备完成后，由用户执行一次明确说明的双客户端原生 UI 操作序列；在用户完成或报告失败前不把
  SC-2/SC-3 标为通过。

## Assumptions

- 当前机器可同时启动两个独立 A21 客户端会话并使用两个可登录角色；若客户端单实例、账号或外部登录状态
  阻止双会话，必须先用直接证据确认并穷尽本地安全替代，不能用合成 UI 证据代替。
