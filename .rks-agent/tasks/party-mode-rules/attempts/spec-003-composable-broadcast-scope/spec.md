---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 可组合广播作用域与附近角色频道隔离

## Why and Outcome

所有依赖“周围在线角色”的服务端行为必须使用同一套可组合接收者约束。调用方既能表达全服或单频道，
也能把频道、城镇、区域、实际站立状态和共享空间组合为交集；当前 A21 的附近角色名单与广播统一采用
同频道约束，使相同 town/area 数字落在不同 Game listener 的角色互不可见。

## Scope

包含通用在线会话作用域、presence 查询、通知 audience 解析，以及当前全部城镇附近角色进入、发布、
跨区、移动和离开消费者的迁移。保留精确角色 audience，并以自动化验证组合语义和频道隔离。

不包含公开队伍目录的多队伍编码/差分发布，不新增具体全服或频道业务消息，不改变 Party 成员定向通知，
也不包含 Story/Cycle、P2P、战斗或奖励。

## Definitions and Invariants

- `scope` 是对当前在线 presence 集合的一组接收者约束；多个维度一律取交集，不存在优先级或任选其一。
- 可组合维度至少包含：channel、town、area、`StandingInTown`、`SharesTownPresence`；另可精确排除一个
  origin character。没有维度和锚点的 scope 表示所有已登记的在线 presence。
- channel 的锚点是实际 Game listener port。town/area 比较 wire 值；area 不能跨 town 解释，因此启用
  area 时必须同时启用 town 并提供两者锚点。
- 每个需要数值锚点的维度必须提供对应值；提供了未启用维度的锚点同样是调用错误。非法 scope 在构造或
  查询边界抛出确定性异常，不得静默扩散为更宽 audience。
- Presence query 的结果按 `CharacterId` 稳定排序并保留记录中的 exact `SelectedSessionIdentity`；筛选不
  将会话降级为仅 character id。
- 动态范围 audience 在通知解析时查询当前 presence 和当前 selected session；离线或 presence/session
  generation 不一致的角色不是接收者。`TheseCharacters` 则保持已接受的输入顺序、重复项和按当前选中
  会话解析语义。
- 需要用同一瞬时快照同时构造消息体名单和接收者的路径，必须先查询一次并冻结 exact session 集合；
  不得降级为会在发送时跟随 replacement 的 character 集合，也不得分别动态解析而让 body 与 audience
  指向不同群体。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 提供一个消息主题无关的 scope contract，能表达全服、同频道、同城镇、同区域、站在城镇、共享空间和精确排除，并确定性校验锚点与组合。 |
| RQ-002 | Presence 服务以一个 scope 查询所有约束同时成立的当前记录，返回稳定有序的 exact-session 结果；旧的松散 area/listener 查询不再成为生产可见性入口。 |
| RQ-003 | Notification audience 支持同一 scope contract 的发送时解析及 exact-session 冻结投递，且不改变 `TheseCharacters` 的现有 replacement 语义或 NotificationBus 的逐接收者重检保护。 |
| RQ-004 | 相邻状态加载、选角后的双方发布、跨区域旧/新区域发布、区域内移动和会话清理全部使用同频道 + 同 town + 同 area + standing/shared 的可见性约束，必要时排除自己。 |
| RQ-005 | 结果保持城镇/组队/通知回归与分层，不声称已实现公开队伍目录或任何具体的全服/频道消息。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 给定空维度且无锚点，scope 合法并表示全部 presence；给定 channel、town、town+area 或其与 standing/shared/exclusion 的任意合法组合，每个约束均参与交集；缺 listener/town/area、area 未带 town、或多余锚点均确定性失败。 |
| AC-002 | RQ-002 | 在至少两个 listener、两个 town/area、站立/非站立、共享/私有和同角色替代 generation 的数据集中，查询只返回全部条件满足者、排除指定角色、按 ID 稳定排序，并保留匹配记录的 exact session。 |
| AC-003 | RQ-003 | 范围 audience 只解析 presence 与当前 selected session 相互匹配的角色；exact-session audience 在解析、逐接收者重检和重试时都不跟随 replacement。`TheseCharacters` 的顺序、重复项、离线忽略和替代会话投递回归保持通过。 |
| AC-004 | RQ-004 | 六个生产查询点都有同频道正例和跨频道同 town/area 反例；跨频道或旧 generation 角色既不进入新人/AREA_USERS 名单，也不收到 arrival/departure/position 通知，同频道同区域角色仍获得原有内容，body 中的名单与 frozen exact audience 一致。 |
| AC-005 | RQ-005 | Release build 0 warning/error，全量适用测试逐项 Passed，Application 不引用 Infrastructure；独立完整结果审查确认没有改变 Party 精确 audience、协议 body 或引入公开目录/Story/Cycle。 |

## Success Evidence

- EV-001（AC-001/002）：scope 值对象与 presence query 的组合矩阵、非法输入、排序和 generation 测试。
- EV-002（AC-003）：audience resolver 与 NotificationBus 的动态会话正反回归。
- EV-003（AC-004）：SessionAdjacent、SelectCharacter town publication、SetUserArea、SetUserPosition、cleanup
  的跨 listener/同 listener 行为测试与完整调用点扫描。
- EV-004（AC-005）：完整结果独立审查、审查后 fresh build/test/diff checks 和完成验证工件。
