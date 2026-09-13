---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 固定 A21 原生公共队伍目录运行时闭环

## Outcome

固定当前 A21 客户端中，一名非队员 observer 在与 leader 同一 `VisibleTownArea` 时，通过正常网络与客户端
逻辑在原生 ID `0x11` 主目录看见该队伍；服务端 party 变化会刷新同一行，解散或 visibility departure 会
移除该行。结论必须同时由 exact-session wire/Host、客户端对象/目录状态和用户原生 UI 观察支持。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 运行前固定 Host、两个客户端、模块/配置 hash、account/character 与 listener/channel/town/area/generation；旧进程和旧日志不能混入本轮。 |
| RQ-002 | leader 的 CNUser `0x0002` 必须先到 observer，party `0x0009 type-0` 随后到达并关联；ID `0x11` 以 leader 为代表新增唯一行。 |
| RQ-003 | settings 或 membership 变化刷新同一 party 行；disband 或 leader 离开可见域删除该行，不留下重复、幽灵或过期行。 |
| RQ-004 | exact-session 与 `VisibleTownArea` 边界保持；replacement/其他 scope 不接收陈旧投影，满员状态仍保留并仅由客户端呈现为不可加入。 |
| RQ-005 | 若 add/update/delete 任一阶段失败，证据须定位为 Host、wire、CNUser、CParty/association、flush/filter、`this+0x188` 或 renderer/UI 的最早断点；只修复已证的本范围原因并从干净边界重验。 |
| RQ-006 | 运行时结论、协议置信度、实现与自动化一致；不新增 ID `0x11` 的 `0x023C` 路径，并通过完整独立审查与审查后验证。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | preflight 记录当前 B1/ImageBase、DNF/ClientHack/plugin/config/Host hashes，两客户端独立目录与登录身份、可用端口、日志零点和可逆恢复边界；缺项时不请求用户动作。 |
| AC-002 | RQ-002 | 同一 observer 时间线中，leader 的 `0x0002 subtype 0/3/6` 先于包含该 leader/partyId 的 `0x0009 type-0`；客户端可回读 CNUser 与 CParty association，ID `0x11` flush 后 `this+0x188`/等价容器含该 partyId，用户确认原生行可见且字段归属一致。 |
| AC-003 | RQ-003 | 一次 settings 或 membership mutation 产生当前 revision 的 observer `0x0009`，客户端容器和用户可见行随之改变；一次 disband 或 departure 产生 `0x0009 type-3` 或相应 CNUser removal，flush 后容器与原生行均消失。 |
| AC-004 | RQ-004 | 自动化与本轮 runtime identity 共同证明成员不收 observer 重复、旧 exact 不命中 replacement、不同 scope 不泄漏；静态客户端门和服务端编码证明满员 snapshot 仍投影而 UI 仅禁用加入。不得把未运行的附加 UI 场景标为 ClientVerified。 |
| AC-005 | RQ-005 | 每个失败都有最早已观察断点、原始证据和明确 stop/retry 决定；任何修复先有 faithful RED，再通过目标 runtime 和必要回归。若必需外部输入不可用，保留 `manual_required/blocked`，不得用合成成功替代。 |
| AC-006 | RQ-006 | runtime evidence manifest 可重放，协议页/catalog 只升级已观察范围；Release build、受影响与全量回归、零新增目录 `0x023C`、独立 mixed-result review 和 fresh completion verification 全部通过。 |

## Native scenario

### Add

Given 两个 exact sessions 已进入同一 listener/channel/town/area，observer 已持有 leader CNUser，且二者不在
同一队伍；when leader 通过原生组队流程创建普通队伍并由 observer 打开/刷新 ID `0x11`；then observer 的
wire、客户端 association、目录容器和原生 UI 都指向同一个 partyId/leader。

### Update

Given 上述行已出现；when leader 更改一个客户端可呈现的 party setting 或发生可呈现的成员变化；then
observer 只保留一条同 party 行，并在本轮 mutation 后显示当前值或人数。

### Delete

Given 更新后的行仍存在；when leader 解散队伍，或作为明确替代场景离开该 visibility scope；then observer
处理 clear/removal 后，目录容器与原生 ID `0x11` 均不再显示该 party。

## Evidence boundaries

- `ClientVerified` 的最低组合是：当前 hash + exact-session `0x0002/0x0009` + 客户端对象/目录状态 + 用户原生
  UI 观察。单独的静态调用链、自动化、Host send 或 frame capture 只能支持其自身层级。
- UI 观察记录 party leader、可辨识标题/人数与 add/update/delete 时点；不记录或暴露登录凭据。
- 一次用户请求可以包含完整 add/update/delete 动作序列；开始前说明探针已就绪，完成/失败后即停止操作并回报
  唯一约定信号。

## Invariants

- 使用接受基线 `365baed0` 的正常服务端与客户端逻辑，不注入伪造目录行、不跳过 filter/renderer。
- `0x0009` 不创建缺失 CNUser；任何 add 证明都必须先有对应 `0x0002`。
- 旧日志、旧 session、另一客户端目录或另一 partyId 的命中不计入当前场景。
- ID `0x14`/`0x023C`、快速组队、Story/Cycle 与 PVP 不作为 ID `0x11` 成功条件。
