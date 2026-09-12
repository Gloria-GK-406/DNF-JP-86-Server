# TASK-03 — `0x000C` 严格入口与设置通知闭环

## Task Unit Contract

- Revision: 1。
- Outcome and references: 交付 `SET_PARTY_INFO (0x000C)` 的双形状严格 decoder、typed Command/
  handler/注册，以及新建完整状态与编辑 `NOTI 0x0009 type=1` 的有序投影，覆盖 Plan T-003、
  RQ-004/RQ-005/RQ-007/RQ-008/RQ-009 与 AC-003/AC-004/AC-006/AC-007/AC-008。
- Owned boundary: `Server/src/Dfo.Server.Protocol/Game/Party/`、必要的 Game type/message enums 与
  `ProtocolModule` 注册、对应 Protocol tests；可机械调整受新消息类型影响的既有测试。不得实现未知
  `0x000C` response、非空 S2C 标题回显、`hasExtra!=0`，不得赋予 Story/Cycle/Normal 玩法语义。
- Dependencies: TASK-01 已审查的 exact direct `12B` / edit `16+n` 与 type-1 parser 证据；TASK-02 已
  审查的不可变设置、规则服务、原子领域 API 和 Created/Updated 通知 kind。
- Required checks: 先记录 decoder/handler/encoder/delivery 行为 RED；逐字节覆盖两个合法形状和
  12B/28B 证据向量，负长、溢出、截断、尾随、非法 UTF-8、非法 edit gate、类别/容量规则拒绝；当前
  会话 gate、Command-only mutation、创建/编辑 audience/order、type-0 除 category 外的回归向量、
  type-1 info0 分支、迟到旧 revision 抑制、生产注册；运行 Party Protocol、完整 Protocol 与 Release
  build，解析 TRX 并 `git diff --check`。

## Detailed Plan

1. 先在 Party codec tests 写 RED：direct 样本 `00 03 02 00 00 00 00 05 00 02 00 00` 原样归一化，
   edit 样本 `01 00 0C 00 00 00 <12B UTF-8> 04 00 00 00 00 05 00 02 01 00` 归一化为
   `01 00 04 00 00 00 00 05 00 02 01 00`；所有长度、UTF-8、edit `[0] <= 2/[1] == 0` 与规则边界
   拒绝必须在构造请求前发生。
2. 增加 `SetPartyInfoRequest` 与 `SetPartyInfoDecoder`。decoder 只识别 exact 12B 或 exact `16+n`；长度
   使用宽算术防溢出，严格 UTF-8 只作输入合法性判据，标题仍以不可变原始字节进入领域；category/
   capacity 通过 `IPartyModeRuleService.Validate(settings, 0)`，不复制业务枚举或范围。
3. 增加 `SetPartyInfoCommand`/handler，仅调用 `IPartyDomainService.CreateOrUpdateSettingsExact`；消息 handler
   按连接解析当前 selected session，持有该账号 `SelectedSessionTransitionGate` 后重读精确 identity，
   再派发 Command。成功后释放 gate 并把不可变 plan 交给 `PartyNotificationDelivery`；失败只记录，不猜
   客户端 response body。
4. 抽取共享信息块编码：写 `settings[0]`，仅当其为 0 写空 raw-dstr 长度 0，再写 `[1..11]`。现有
   type-0 roster 改为读取 snapshot settings，长度随 info0 为 65/61；默认向量只把规范 `[9]` 从旧 0
   改为 Normal 2，其余名册字节不变。标题回显证据不足，仍固定空 raw-dstr。
5. 新增 type-1 message/encoder：`u16 count=1 + u16 partyId + u8 type=1 + 信息块 + u8 hasExtra=0`；
   info0=0 为 22B，非零为 18B，逐字节测试 category/target/未知字节均来自同一 snapshot。
6. Delivery 对 `SettingsChange=Created` 走当前完整单人 party state phases；对 Updated 只向不可变 snapshot
   中仍为当前代际的成员发布一份 type-1。两者继续共用 generation stream/high-water mark，迟到旧
   revision 不得覆盖新设置；普通邀请/离队/查询路径保持原顺序。
7. 注册 C2S type、decoder、handler、Command handler、S2C message encoder 与 notification handler；
   DI 测试证明可达且没有第二份规则判断。完成聚焦 GREEN、完整 Protocol（提供 PVF 路径）、Release
   build、TRX 逐项统计与 diff check。

## Result

### Changed

- 新增 `SetPartyInfoRequest`/strict `SetPartyInfoDecoder`、typed Command/handler 和生产注册。direct 只接收
  exact 12B；edit 只接收 exact `16+n`，以宽算术校验 signed title length、严格 UTF-8、edit gate，随后
  将首两字节与尾 10 字节规范化为同一 12B 设置并统一调用 `IPartyModeRuleService.Validate`。
- `SetPartyInfoHandler` 只负责 selected-session transition gate、gate 内精确 identity 重读与 Command
  派发；领域成功后先释放 gate，再投影不可变通知计划。协议失败不臆造 `0x000C` response。
- 新增 `NOTI 0x0009 type=1` 的 `PartySettingsMessage`/encoder/notification handler：编码
  `count=1 + partyId + type=1 + shared information block + hasExtra=0`，`info0=0` 为 22B，非零为
  18B。非空 S2C 标题仍不猜测，条件 raw-dstring 固定编码为空。
- type-0 roster encoder 改为消费同 revision 的 `PartySnapshot.Settings`；默认 65B 向量仅把 category
  修正为 Normal (`2`)，`info0!=0` 分支为 61B，其余字段与既有名册布局保持一致。
- `PartyNotificationDelivery` 对 Created 保持完整 state phases，对 Updated 只向 snapshot 成员发布一份
  settings 通知；继续共用 generation high-water stream，迟到旧 revision 被抑制。Updated 计划在任何
  publication 之前检查并拒绝与 invite/clear/accept 混合，避免异常计划产生部分副作用。

### Why

当前 A21 已证明的两种 C2S 形状与 type-1 conditional block 现在形成一条 typed、严格、可测试的闭环；
所有设置合法性仍由 TASK-02 的唯一规则服务裁决，协议层只处理 wire shape 和 current-session 编排。
创建与编辑的客户端通知面被显式区分，同时不把未知标题回显或模式玩法含义伪装成已确认事实。

### Checks

- TDD RED 见 `evidence/task03/task03-red.txt`：缺少 request/decoder/handler/type/encoder/notification 类型
  时分别编译失败；另以实际失败用例证明异常 Updated 计划曾先发布 invitation，随后才前移校验。
- Party Protocol 聚焦 TRX 逐个 `UnitTestResult` 解析：`Passed=67`、`Failed=0`、`Skipped=0`。
- 带 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 的完整 Protocol TRX：
  `Passed=792`、`Failed=0`、`Skipped=0`。
- Application Party 回归 TRX：`Passed=30`、`Failed=0`、`Skipped=0`。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore`：成功，0 warning / 0 error；staged 与
  unstaged `git diff --check` 均通过。

### Remaining

- 等待 TASK-03 独立 quick review；通过后标记 done，并进入 TASK-04 的集成资料与当前 A21 实机抓包。
- current A21 create/edit 的真实帧及 UI 行为尚未采集，因此本单元不提升 `0x000C`/type-1 的
  `ClientVerified` 状态，也不实现非空 S2C 标题或 Story/Cycle/Normal 玩法语义。

## Independent Quick Review — Round 1

```yaml
kind: rks-review
verdict: changes_required
```

- P1：若 Updated revision 2 比 Created revision 1 更早进入 delivery stream，旧 Created 会被 high-water
  抑制，而首次 Updated 只发 type-1；客户端因此永远收不到该 generation 的完整 realtime/endpoints/roster。
- P2：pre-publication shape validation 只覆盖 Updated 的部分互斥字段；Created+Invitation、孤立 clear
  metadata、缺 snapshot、未知 enum 等异常计划仍可能先发布或被静默接受。
- 其余 decoder 边界、规则唯一性、gate/Command、type-0/type-1 编码、audience、DI 与证据边界通过。

## Result — Round 2

### Changed

- `PartyDeliveryStream` 记录该 generation 是否已成功投影完整状态；首次到达的 Updated 若尚无完整基线，
  使用其最新 snapshot 提升为完整三阶段投影。之后迟到的旧 Created 可安全由 high-water 抑制；已有基线
  的正常 Updated 仍只发一份 type-1。
- `ValidatePlanShape` 现在在任何 invitation/state publication 之前统一验证：settings kind 只能是
  Created/Updated；两者必须只携带 snapshot；invitation 必须独占；acceptance 必须只配 resulting snapshot；
  clear audience 与 partyId/generation/revision 必须全有或全无。非法计划统一零 publication。
- 新增反序 Updated→Created 回归，证明只发送 revision 2 的完整状态；异常计划矩阵覆盖 Created 混合、
  缺 snapshot、孤立/残缺 clear metadata、未知 settings kind 和无 snapshot acceptance。

### Checks

- remediation RED：两条新测试初次运行 `Failed=2`，分别复现缺完整基线和 shape 漏洞，见
  `evidence/task03/task03-red.txt`。
- fresh Party Protocol TRX 逐项：`Passed=68`、无 failed/skipped。
- fresh 完整 Protocol TRX 逐项：`Passed=793`、无 failed/skipped。
- fresh Application Party TRX 逐项：`Passed=30`、无 failed/skipped。
- fresh Release solution build：0 warning / 0 error；修正空白后 staged/unstaged `git diff --check` 均通过。

### Remaining

- 等待 Round 2 fresh quick review；通过后标记 TASK-03 done 并开始 TASK-04。

## Independent Quick Review — Round 2

```yaml
kind: rks-review
verdict: approved
```

- Reviewer 复核首次 Updated 提升完整基线、迟到 Created 抑制、正常 Created→Updated 增量路径，以及
  Created/Updated/invitation/acceptance/clear metadata/未知 enum 的全量 pre-publication shape validation；
  P1/P2 均已闭合，无新阻断项。
- Reviewer fresh checks：`PartyNotificationTests 16/16`、Protocol Party `68/68`、完整 Protocol
  `793/793`、Application Party `30/30`，staged/unstaged diff check 均通过。
