# TASK-03 — 黑名单协议 0x0076 – 0x0078

## Task Unit Contract

- Revision: 1
- Outcome and references: Plan T-003、SD-5。玩家可以拉黑、解除拉黑、拉取黑名单列表，并提供 Game 层共享的「黑名单列表应答」构建，供 TASK-04 在全量好友列表之后补发。覆盖 RQ-037、RQ-039、RQ-040（黑名单部分）及 AM-1 中 0x0077/0x0078 名字 1–30 字节的约束；验收 AC-013（协议面）、AC-030（黑名单部分）。
- Owned boundary:
  - 新增 `Server/src/Dfo.Server.Protocol/Game/Blacklist/` 切片；
  - Game 层共享文件 `Server/src/Dfo.Server.Protocol/Game/BlacklistFrames.cs`，放黑名单列表应答消息、编码与构建；
  - `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`：新增 `0x0076`、`0x0077` 目录成员；
  - `ProtocolModule.cs`：加一行切片调用；
  - `UnansweredReports/`：移除 `0x0078` 的 decoder、报告类型与遥测登记；
  - 测试放 `Server/tests/Dfo.Server.Protocol.Tests/Game/Blacklist/`，必要时放 Application.Tests 中对应的命令测试目录；
  - 协议页 `0x0076.md`、`0x0077.md`、`0x0078.md` 人工区的实现状态，以及 `protocol/catalog.md` 中这三行。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 通过；
  - Protocol.Tests、Application.Tests、Host.Tests 全部通过，其中 OpcodeArchitectureTests、CommandHandlerArchitectureTests、消息与命令闭合检查都在内；
  - 新增字节级测试覆盖每个成功形态与错误码、空列表、畸形请求；
  - 命令测试覆盖：拉黑成功后列表含目标；重复、不存在、已满、拉黑自己、解除不存在的各条拒绝路径；列表日期换算。
- Dependencies: TASK-01, TASK-02
- Result review: required — TASK-04 要复用这里的共享黑名单列表构建，好友全量之后补发依赖它的字节正确；而且它改动了 opcode 目录与 UnansweredReports 的注册，错误会表现为启动失败或静默丢包。

## Detailed Plan

**线上格式**：依据 Spec RQ-037、RQ-039 与 AM-1、`research/client-evidence.md`。DSTR = `[len:u32][UTF-8]`。

- `0x0076` 请求：`[name:DSTR]`。
  - 成功：`[1][name:DSTR]`，name 回写目标角色的当前名，1–255 字节。
  - 失败：`[0][reason]`。重复 74，不存在或已删除 76，已满 78；拉黑自己、畸形请求、发起者不在世界中，一律回 0。
- `0x0077` 请求：`[name:DSTR]`。
  - 成功：`[1][name:DSTR]`，名字 1–30 字节。
  - 失败：`[0][75]`，包括名字查不到角色、关系不存在、畸形请求。
- `0x0078` 请求：空 body。应答 `[1][count:u8]`，后接 count 条 `[name:DSTR 1–30B][年-1900:u8][月0起:u8][日:u8]`。
  - 日期：取加入时间 `Since`，按服务器本地时区换算（`TimeProvider.LocalTimeZone`）。
  - 名字超过 30 字节的条目跳过，并记 Warning（只可能来自旧库：本服创建上限 18 字节，见 `CharacterCreationRuleService`）。
  - 条数不超过 64，在 u8 范围内。

**结构**
- 请求解码放在切片内：一个 payload codec，加三个 decoder、三个 Messages record。参照 `Game/CharacterName/` 与 `Game/OneToOneChat/` 的拆分方式。
- 薄 handler 交给 typed CMD。CMD 用 `ISelectedSessionDomainService.FindByConnection` 加 `IsInWorld` 确认发起者；不在世界中时：
  - 0x0076、0x0077 回失败（0／75）；
  - 0x0078 回空列表 `[1][0]`，因为客户端在回城路上会请求它。
- 名字解析：在 `ReadOnly` 单元里调 `ICharacterIdentityQueryDomainService.FindByNameAsync`。
- 写入：在 `units.WriteAsync` 单元里调 `ICharacterRelationDomainService.AddAsync` 或 `RemoveAsync`，kind 为 `Blacklist`。
- 结果映射（领域结果 → 线上码）放在切片 codec 内，Application 不知道线上码。
- 回包：`IGameCommandOutbox.RegisterCmdAsync`。
- 共享列表构建 `BlacklistFrames.cs`，按 SD-5 放 Game 层：
  - 消息 record `BlacklistListResponse`（CMD 0x0078）及其编码器；
  - 一个只读构建函数，按「关系只读服务 + 身份批量读取 + 时区」生成条目。0x0078 的 CMD 与 TASK-04 的补发都调它。
  - 编码器在切片注册里登记一次。若出站注册必须跟着某个切片，登记在本切片里，供全局使用。
- `NetworkMessages.cs` 增加 `GameRegisterBlacklist = 0x0001_0076`、`GameDeleteBlacklist = 0x0001_0077`；现有 `GameRequestBlacklist` 改由本切片注册，`UnansweredReports` 中对应的三处（报告类型、decoder、遥测 handler）一并删除。

**测试**（测试先行）
- **codec**：三种请求的解码，含截断、尾随数据、空名；三种应答的编码，含成功、各错误码、空列表、多条列表与日期字节。
- **命令**：参照 `Application.Tests/Chat/SendChatCommandTests` 一类的夹具（真实 SQLite 加 outbox 捕获）：
  - 拉黑离线角色成功，列表含该角色；
  - 74、76、78 与拉黑自己（回 0）；
  - 解除成功；解除不存在回 75；
  - 0x0078 日期按本地时区换算。

**文档**：协议页人工区追加「实现：本仓 CurrentImplementation」小节，指向切片与测试；catalog 中三行改为已实现。

## Result

- Outcome: done
- Round: 1，worker 实现；owned change 为当前未提交 diff，TASK-04.md 除外（TASK-04 是另一个尚未开始的单元）。
- Changed:
  - 切片 `Game/Blacklist/`：Messages、PayloadCodec、三个 CMD 与薄 handler、Registration。
  - Game 层共享文件 `Game/BlacklistFrames.cs`：`BlacklistListEntry`、`BlacklistListResponse`（NameLimit 30）、`BlacklistListing`，以及 `BlacklistListBuilder.BuildAsync(owner, relations, identities, zone, ct)`；被跳过的条目通过 `Skipped` 返回给调用方。
  - `NetworkMessages.cs`：新增客户端成员 0x0076/0x0077；新增三个服务端应答成员 0x0076/0x0077/0x0078（计划漏写，应答需要服务端类型）。
  - `ProtocolModule`；删除 `UnansweredReports` 中 0x0078 的三处登记。
  - 协议页：`0x0076`/`0x0077`/`0x0078` 人工区的实现小节；0x0078 页删掉了已过时的「有意不回」论述。
  - `catalog.md`：黑名单小节、C→S 进度表三行、覆盖区三条 decoder 与三条 encoder。
  - 测试：`Protocol.Tests/Game/Blacklist/` 两个文件（字节级、构建函数），`Application.Tests/BlacklistCommands/BlacklistCommandTests.cs`（ShopFixture，真实 SQLite + 派发 + 发件箱）。
- Why:
  - 0x0077 名字超过 30 字节时，照常解除，但回 `[0][75]` 并记 Warning。客户端读不了超长的成功应答，而 75 的提示「重连后刷新列表」在解除之后正好成立。这种名字只会出现在旧库里。
  - 0x0078 请求带尾随字节时照常应答：请求本身没有字段。
  - 命令测试目录取名 `BlacklistCommands`，避免与命名空间同名引发 CS0118。
  - 共享构建由调用方传入时区：NOTI 处理器不能注入 `TimeProvider`。补发路径（TASK-06）需要把时区带进通知。
- Checks:
  - build：0 警告，0 错误；
  - Protocol.Tests：2172 通过，60 跳过（原有），新增 33；
  - Application.Tests：5947 通过，594 跳过（原有），新增 17；
  - Host.Tests：230 通过，14 跳过；
  - Networking.Tests：由主代理补跑，见下；
  - 变异检查：把时区改成 UTC 后，对应测试失败。
- Remaining:
  - `protocol/patterns/data/check-server-documentation-sync.py` 在本分支报 459 条与本任务无关的失败（decoder 解析数为 0），因此 catalog 覆盖区是手改的；
  - `GameProtocol.RequestBlacklistType` 已无人引用，留待清理。
- Networking.Tests (re-run by the main agent): 189 passed, 26 skipped, 0 failed.
- Quick review: approved (independent context). Two of its non-blocking points were fixed before this commit:
  - the `BlacklistListBuilder` remark now says that notification handlers may call it without a transaction;
  - `protocol/patterns/world-entry.md` now carries a correction note saying 0x0078 is answered as of #60 (the stale record, fixed across the task boundary).
- Open for TASK-06: the time zone must travel with any notification that re-sends the blacklist. Pass a zone id, not a `TimeZoneInfo`, if the notification is persisted.
