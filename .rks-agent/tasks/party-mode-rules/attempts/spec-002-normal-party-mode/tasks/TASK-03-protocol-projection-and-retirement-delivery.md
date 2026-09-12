# TASK-03 — 协议字段投影、标题编码与队伍生命周期通知

## Task Unit Contract

- Revision: 2 (current-A21 correction; identifies this contract and its detailed plan)
- Outcome and references: 消费 TASK-02 的 canonical Normal/retirement 结果，交付 Plan T-003、
  RQ-003..RQ-005、AC-002..AC-005/AC-007 的协议结果：C2S `0x000C` 前导字段转换为 S2C canonical
  顺序；预设标题索引与 inline `PartyTitle` 正确回送；当前 A21 的队长离队只投影旧 generation clear。
- Owned boundary: `Dfo.Server.Protocol/Game/Party` 的 `SetPartyInfoDecoder`、队伍信息块 encoder、
  `PartyNotificationDelivery` 及其最小相邻消息类型；`Dfo.Server.Protocol.Tests/Game/Party` 和直接受影响的
  cleanup notification 测试；本 Task 文件。不得修改 Application 领域规则/生命周期、公开队伍目录、
  Story/Cycle 专属规则或协议资料。
- Required checks: 先以失败测试固定 12-byte/inline-title C2S→canonical、预设标题与 raw-dstr 的
  byte-exact S2C、retirement clear→new full-state 顺序、重复/迟到旧 generation 抑制与 invalid plan shape；
  随后跑 Protocol Party 聚焦测试、直接受影响 cleanup tests、完整 Protocol 测试、分层断言及
  `git diff --check`。
- Dependencies: TASK-01 `evidence/task01/runtime-verdict.md` 的字段交换/标题证据；TASK-02 的
  `PartyRetirement(old party id/generation/revision/audience)` + replacement `PartySnapshot`，以及通过的
  Application 领域生命周期结果。

## Detailed Plan

1. 记录现有 Protocol Party 与 cleanup notification 聚焦测试基线。新增 RED decoder 用例：
   - 当前真实 create `00 03 ... category=2,target=0` / `00 06 ...` 分别成为 canonical `03 00 ...` /
     `06 00 ...`；12-byte 尾部保持逐字节不变；
   - inline-title 形状把 C2S `[specialFlag,titleIndex=0]` 转为 canonical `[titleIndex=0,specialFlag]`，
     UTF-8/长度/leading gate 的拒绝边界保持严格；规则验证针对转换后的 canonical settings；
   - decoder 不承担领域 normalization，canonical `[1]` 是否归零仍由 TASK-02 rule service 处理。
2. 调整 `SetPartyInfoDecoder`，只在完成形状和长度校验后交换前两字段：固定 12-byte 输入交换 `[0]/[1]`
   并复制 `[2..]`；inline 输入令 canonical `[0]=body[1]`、`[1]=body[0]`，再把 dstr 后十字节复制到
   canonical `[2..]`。保留 checked 长度、严格 UTF-8 和无 trailing bytes 约束，不从预设索引猜文案。
3. 为 `PartyInformationBlockEncoder` 增加显式 `PartyTitle` 输入，并让 roster/settings 两条调用都使用
   `snapshot.Title`。canonical titleIndex 非零时不写 raw-dstr；为零时写 `i32 byteLength + UTF-8 bytes`，
   然后原样写 canonical `[1..11]`。增加 byte-exact 测试覆盖空标题、非空多字节 UTF-8 和预设索引，
   保证标题与 settings 分属两个值且无隐式清除。
4. 在 `PartyNotificationDelivery.ValidatePlanShape` 明确定义 retirement 组合：只能与 replacement snapshot
   共存，旧/new party id 和 generation 必须不同，retirement audience 非空且旧 version 元数据完整；
   不与 invitation、acceptance、settings-only change 或同-generation member clear 混用。非法组合必须在
   首次 publication 前失败。
5. 把 retirement 作为旧 generation 的终结 revision 送入其独立 delivery stream：获得旧 stream gate，
   仅当 revision 更新时先向精确 retirement audience 发布旧 party id 的 type-3 clear，再更新该 stream
   tombstone 并清空成员/full-state 集合；重复或迟到 retirement 不重发。完成旧 stream 后再按既有新
   generation stream 流程投影 replacement snapshot 的 realtime → endpoints → roster 完整状态。
6. 增加顺序与乱序 RED/GREEN 用例：先建立旧 generation，交付 transfer plan 后断言 old clear 是第一条、
   new full-state audience 只含幸存者且使用新 snapshot；随后交付迟到旧 snapshot、重复 transfer 及旧
   realtime，均不得复活或覆盖。保留并重跑现有四种同-generation 乱序反例、settings promotion、
   member-leave clear 与 acceptance 顺序。
7. 运行 Required checks并审阅实际 diff；在本文件追加 Changed / Why / Checks / Remaining。明确把最终
   A21 创建标题、队长转移、最后成员解散实机验证与协议资料升级留给 TASK-04，不把自动化通过写成
   `ClientVerified`。

## Result — Round 1

### Changed

- `SetPartyInfoDecoder` 对 direct 与 inline-title 两种 C2S 形状都交换前两字段，再构造 canonical
  `PartySettings`；尾部十字节、严格长度和 UTF-8 拒绝边界保持不变。
- `PartyInformationBlockEncoder` 现在接收 `PartyTitle`：canonical titleIndex 为 0 时写入
  `i32 byteLength + raw UTF-8 bytes`，非零预设索引分支不写 raw-dstr；roster/type-1 settings 两条投影
  都传入同一 snapshot title。
- `PartyNotificationDelivery` 验证 retirement 只能携带不同 party id/generation 的 replacement snapshot，
  并先在旧 generation stream 发布 type-3 clear/tombstone，再由新 generation stream 发布完整状态。
  重复 retirement、迟到旧 snapshot 与旧 realtime 均被旧 tombstone 抑制。
- byte-exact 与顺序测试新增真实 `00 03`/`00 06` create、inline 多字节标题、预设索引、roster 标题、
  invalid retirement shape 和 leader transfer old-clear → new-state/late replay 用例。

### Why

TASK-01 已证明 C2S `[specialFlag,titleIndex]` 与 S2C `[titleIndex,specialFlag]` 方向相反；旧实现原样复制后
又清 `[1]`，正好把预设索引抹掉。TASK-02 同时把多人队长离开表达为 old retirement + replacement，
因此协议层必须把两个 generation 分开排序，不能把旧 clear 元数据硬塞进新 snapshot stream。

### Checks

- Protocol Party + cleanup notification 聚焦测试：82 Passed / 0 Failed。
- 完整 `Dfo.Server.Protocol.Tests`（设置 `PVF_ARCHIVE_PATH` 指向固定 A21 `Script.pvf`）：
  801 Passed / 0 Failed。
- Application architecture 聚焦测试：29 Passed / 0 Failed。
- `git diff --check`：Passed；实际产品代码 diff 限于 Task owned boundary。

### Remaining

- TASK-04 需在最终实现上用当前 A21 复测预设标题可见、inline 标题（若 UI 提供）、多人队长转移和最后
  成员解散，并以定向帧/Host/UI 三方对齐决定证据等级。
- 协议专页、sample/trace/index/catalog 与最终 Release build/全量测试仍由 TASK-04 完成；公开队伍目录
  保持 Spec 外，不在本轮实现。

## Result Correction — Round 2

Round 1 的 old-clear → replacement full-state 投影被当前 A21 UI 证据推翻，TASK-04 已删除
`PartyRetirement`、retirement delivery 与对应测试。C2S 字段交换、预设标题及 raw-dstr 编码是独立证据
链，继续保留；队长离队回到既有旧 generation type-3 clear 路径。
