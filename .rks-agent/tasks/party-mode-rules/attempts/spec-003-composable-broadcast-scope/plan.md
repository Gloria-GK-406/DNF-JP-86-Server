---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 可组合广播作用域与附近角色频道隔离

## Planning Choice

采用 direct Plan。值对象、presence query、audience resolver 与六个现有消费者构成一条顺序明确的迁移，
不需要分别交接或独立恢复的 Task Unit；细节在本 Plan 中统一维护，结果写入 implementation report。

## Test-Value Decision

跨频道角色不出现在附近 UI、名单或通知中是直接的玩家可观察结果。当前五个遗漏 listener 的调用点会
真实违反它，而现有测试大多只构造一个 listener，完整构建无法发现。以多 listener 数据集断言 query、
名单和 audience 的实际输出是忠实且可重构的 oracle，新增覆盖成本远低于跨频道角色泄漏的排查成本；
因此作用域 contract、resolver 与每条消费者迁移都采用 TDD。类名、构造函数形状或调用次数不作为 RED。

## Approach and Decisions

- 在 `Dfo.Server.Abstractions/Sessions` 定义一个不可变 scope contract：flags 表示 channel/town/area/
  standing/shared，独立可选的 excluded character 表示 self exclusion；数值锚点与 flags 双向校验，
  `area` 必须与 `town` 同时启用。空 flags/anchors 是合法全服 scope。
- 将 `ISessionPresenceDomainService.InArea` 替换为一个 scope query；实现对当前 presence snapshot 逐项取
  交集并按 `CharacterId` 排序。不要保留生产调用可继续绕过的旧松散入口。
- `NotificationAudience` 增加 scope 形态和 exact-session 冻结形态；resolver 注入 presence，先按 scope
  查询，再只接受其 exact session 仍是 selected-session 当前值的记录。exact-session 在解析、逐接收者
  重检和重试时均不跟随 replacement；`TheseCharacters` 分支原样保留。
- 六个 `presence.InArea` 调用点统一创建同 listener + town + area + standing + shared + exclude self
  scope。SelectCharacter/SetUserArea 中同时生成 body 和 audience 的路径仍只查询一次并冻结
  exact-session audience；区域内 position 通知可使用 scope audience，前提是行为测试证明发送时语义正确；
  cleanup 在 withdraw 前冻结旧区域 audience。
- 不为未来公开队伍目录加入 party-specific 字段或缓存；它只消费本次稳定 scope contract。

## Work

### T-001 Scope contract 与 presence query（RQ-001/002）

1. 在 Application 测试中先建立多 listener、多 town/area、standing/shared、排除和排序矩阵；另覆盖
   全服空 scope、每种缺失/多余锚点、area 未配 town 的失败。
2. 运行聚焦测试确认 RED 来自能力缺失。
3. 在 Abstractions 增加 scope/flags，替换 presence 接口；在 Application 实现严格校验、交集查询和稳定
   排序。保持 `SessionPresence.Session` 原样返回。
4. 运行同一测试 GREEN，并迁移所有编译调用点到新 API 的等价单频道约束。

### T-002 Notification scope resolver（RQ-003）

1. 扩展 `NotificationAudienceTests`：同频道/区域组合、全服、离线 presence、selected session 替代、
   presence generation 不一致；保留 `TheseCharacters` 的顺序/重复回归。先观察范围分支 RED。
2. 增加 scope audience、exact-session audience 和 resolver 分支；只把 presence exact session 与当前
   selected session 相等者解析为 recipient，并让 exact recipient 的逐项重检和重试拒绝 replacement；
   未知形态仍抛异常。
3. 运行 resolver 与 NotificationBus 聚焦回归 GREEN。

### T-003 六条附近角色路径频道隔离（RQ-004）

1. 在 `SessionAdjacentEntryTests`、`TownPresencePublicationTests`、
   `TownProjectionHandlerTests`、`SelectedSessionPartyCleanupTests`/cleanup delivery 的现有行为层加入跨
   listener 同 town/area 反例及同 listener 正例；断言新人名单、AREA_USERS、arrival/departure/position
   audience 和 payload 一致。先以当前实现观察预期 RED。
2. 迁移 `SessionAdjacentEntryDomainService.LoadAsync`、`SelectCharacterSessionEntryService`
   `PlanTownPublication`、`SetUserAreaHandler` 旧/新 area、`SetUserPositionHandler`、
   `SelectedSessionCleanupService` 到统一 scope。已有 cleanup 的 listener 行为保持等价。
3. 运行每组聚焦测试 GREEN；扫描生产代码确认旧 `InArea` 调用为 0，且所有附近角色组合均含实际
   listener、town、area、standing/shared 与 self exclusion。

### T-004 集成、审查与完成（RQ-005）

1. 运行 Application/Protocol 的 Session、Notification、TownProjection、SelectCharacter、Cleanup 与
   Party 聚焦回归；执行 Release solution build。
2. 固定 `PVF_ARCHIVE_PATH` 串行运行九个测试项目并解析每个 TRX `UnitTestResult`；运行分层断言、
   `git diff --check` 和仓库边界检查，把结果写入 `implementation-report.md`。
3. 进入配置的独立完整结果审查；解决所有阻塞 findings 后，再按 completion verification 运行审查后
   fresh 检查并封存一个本地接受提交。不合并、不推送、不清理根 worktree。

## Verification

- RED/GREEN 证据记录测试命令、退出状态和直接失败/通过摘要；不把编译夹具错误冒充行为 RED。
- 聚焦测试必须覆盖每个 scope 维度、非法组合、当前/替代 session，以及六个生产查询点的同/跨 listener。
- 全量最终要求 Release build `0 warning / 0 error`，九项目 `0 Failed`，所有非 Passed/Skipped 的 TRX
  outcome 为 0，Application → Infrastructure 分层断言通过。
- 独立审查同时检查代码与交付文档，确认作用域不含消息主题、Party 精确 audience 未迁移、公开目录和
  Story/Cycle 仍为 Out。
