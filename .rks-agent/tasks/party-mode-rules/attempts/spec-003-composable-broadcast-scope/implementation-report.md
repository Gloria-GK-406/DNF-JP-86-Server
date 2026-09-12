---
kind: rks-implementation-report
round: 1
owned_baseline: 73a36166dfd2e6f2da85c8754a91ba724c2eab8f
state: ready_for_review
---

# Implementation Report：可组合广播作用域与附近角色频道隔离

## Changed

- 在 Abstractions 增加消息主题无关的 `SessionPresenceScope` 与 flags，支持全服、频道、城镇、区域、
  实际站立、共享空间和精确排除；构造时拒绝未知 flag、缺失/多余锚点、无 town 的 area 和非法端口。
- Presence 服务用单一 `Query(scope)` 替代旧 `InArea`，按全部启用维度取交集，按 `CharacterId`
  稳定排序，并原样保留记录中的 exact `SelectedSessionIdentity`。
- Notification audience 增加动态 `Scoped` 形态；解析时查询当前 presence，只接受其中 exact session
  仍等于当前 selected session 的角色。未装配 presence 的精确角色通知子系统仍可使用；实际解析范围
  audience 时确定性拒绝。`TheseCharacters` 的输入顺序、重复项和当前会话解析语义保持不变。
- 选角相邻名单、选角双方发布、跨区旧区域离开与新区域进入、区域内移动、会话清理六个生产查询点
  全部迁移到同 listener + town + area + standing + shared + exclude-self 的统一可见范围。需要同时构造
  payload 与 audience 的路径只查询一次并冻结角色集合。
- 新增 scope 组合/非法输入/排序/新代际测试，以及相邻名单、AREA_USERS、arrival/departure/position、
  cleanup 的同频道正例与跨频道反例。

## Why

- A21 的 town/area wire 值会在不同 Game listener 重复；可见空间必须把实际 listener port 作为独立
  channel 维度，否则相同坐标会把跨频道角色混入名单和广播。
- Scope 保持通用约束组合，不携带队伍或消息类型；后续公开队伍目录可以复用同一查询边界而不污染
  presence 模型。本轮没有实现公开队伍目录、Story/Cycle 或新的具体全服/频道消息。
- SelectCharacter、SetUserArea 与 cleanup 继续冻结精确角色 audience，避免消息体名单和动态 audience
  在两个时刻分别查询而分裂；动态 `Scoped` 则提供给只需发送时解析的通知。

## Checks

- 行为 RED：旧相邻查询把跨 listener 角色 `[10,11]` 同时列出；旧 town publication、SetUserArea 和
  SetUserPosition 同样把跨 listener 角色纳入计划/audience。新增范围 audience 在尚无解析分支时以
  `Scoped has no resolution` 失败。
- 聚焦 GREEN：Application scope/adjacent/audience/publication/cleanup 40 项通过；Protocol town
  projection/cleanup 12 项通过；扩充边界矩阵后 scope/audience 24 项通过。
- 调用点扫描：`Server/src` 与 `Server/tests` 中旧 `.InArea(` 为 0；生产代码恰有六个
  `VisibleTownArea(...)` 迁移点。
- Release 构建：`dotnet build Server/86JP.Server.slnx -c Release --no-restore` 为 0 warning / 0 error；
  Launcher Tests Release 构建同为 0 warning / 0 error。
- 设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` 后串行运行九个 Release
  测试项目：3106 Passed、2 Skipped、0 Failed；逐个 TRX 的 `UnitTestResult` 非
  `Passed|NotExecuted` 为 0。
- `git diff --check` 通过；Application 源码/项目文件没有 Infrastructure 引用。

## Remaining

- 本结果仍需配置的独立复审与审查后新鲜完成验证；公开队伍目录由后继 Spec 单独实现。

## Rework Round 2

- 首轮独立审查以 `changes_required` 识别出 replacement 竞态：presence snapshot 降成
  `CharacterId` 后，`TheseCharacters` 会把旧频道事件重定向到同角色的新 session。
- 新增 `TheseSessions` 和 recipient `ExpectedSession`。范围与 exact-session audience 均携带精确
  `SelectedSessionIdentity`；NotificationBus 在每个接收者组合前重检完整 identity，重试队列也保留
  expected session，不跟随 replacement。`TheseCharacters` 及其重连重试语义原样保留。
- 六条附近角色路径在形成 body/audience 前均要求 presence exact session 仍是当前 selected session；
  需要通知的四类计划改为冻结 exact sessions。相邻名单和 town/AREA_USERS body 同样剔除 stale
  generation。
- 新增确定性回归：旧 presence 在 listener 10011、replacement 已到 10012 时不进入相邻/选角/移动
  发布；exact audience 拒绝 replacement；同 connection ID 但 generation/listener 改变时逐接收者重检
  判定 Gone；exact retry 不追随重连，普通 character retry 仍追随。
- Round 2 聚焦验证：Application 96/96、Protocol 13/13，Debug solution build 0 warning / 0 error，
  `git diff --check` 通过。完整 Release 验证留待复审通过后的 completion verification。
