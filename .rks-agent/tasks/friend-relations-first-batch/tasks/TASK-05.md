# TASK-05 — 好友协议 0x0122/0x0123 与登录列表

## Task Unit Contract

- Revision: 1
- Outcome and references: Plan T-004、SD-5。玩家可以按名字加删好友，看到自己的好友列表（含三种在线状态的显示），进入游戏时拿到真实列表；同时提供 Game 层共享的好友帧与列表构建，供 TASK-06 推送复用。覆盖 RQ-030 – RQ-036、RQ-038、RQ-040（好友部分），并按 Spec AM-1、AM-2 执行；验收 AC-030、AC-031。
- Owned boundary:
  - 新增切片 `Server/src/Dfo.Server.Protocol/Game/UnitedFriend/`；
  - Game 层共享文件 `Server/src/Dfo.Server.Protocol/Game/FriendFrames.cs`：全量／删除 NOTI 0x0111、上下线 NOTI 0x0112 的消息、编码器、好友列表构建；
  - `NetworkMessages.cs`：0x0122、0x0123 的客户端与服务端成员，以及 0x0111、0x0112 的 NOTI 成员；
  - `ProtocolModule.cs`：加一行切片调用；
  - 选角进入流程中好友基线的替换：`StateSynchronization/StateSynchronizationMessages.cs` 的 `CrossServerFriendBaselineMessage`、`SelectCharacter/SelectedCharacterStateMessageFactory.cs`、`SelectCharacter/GameSelectCharacterCommand.cs`，只改与好友列表相关的部分；
  - 对应测试：`Protocol.Tests/Game/UnitedFriend/`、Application.Tests 的命令测试目录（避开与命名空间同名，参照 TASK-03 的 `BlacklistCommands`），以及现有选角序列测试中 0x0111 相关的断言；
  - 协议页 `0x0122.md`、`0x0123.md`、`NOTI-0x0111.md`（同时按迁移规则删去 A12 区）、`NOTI-0x0112.md`，以及 `protocol/catalog.md` 的对应行。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx`；
  - Protocol.Tests、Application.Tests、Host.Tests、Networking.Tests 全部通过（Networking.Tests 有真实 TCP 的选角进入序列）；
  - 新增字节级测试覆盖：0x0122/0x0123 请求解码（含畸形）与各应答；0x0111 subcmd 0 的多条与空表、在线／离线 ch、名字 1–255 边界；subcmd 2；0x0112；
  - 命令测试覆盖：加离线好友成功后依序收到 ACK、全量、黑名单列表；重复添加成功且不产生第二条；自己、不存在、已满、server≠1；删除成功后依序收到 ACK、subcmd 2、全量、黑名单列表；删除不存在回 0x15；进入游戏时收到真实列表与黑名单列表。
- Dependencies: TASK-01, TASK-02, TASK-03
- Result review: required — TASK-06 的全部推送都消费这里的共享帧与列表构建；改动还触及选角进入序列，错位会导致客户端整表丢弃或进入失败。

## Detailed Plan

**线上格式**：依据 `research/client-evidence.md`，以及 Spec RQ-030 – RQ-035、AM-1、AM-2。

- 0x0122 请求 `[uid:u16][server:u8][name:DSTR]`，uid 忽略。应答：成功 `[1]`（客户端不读后续）。失败 `[0][reason]`：
  - 目标不存在或已删除：0x15；
  - 已满：0x04；
  - server≠1：0x07（客户端提示「只能加同服务器群」）；
  - 加自己、畸形请求、发起者不在世界中：0x01（客户端静默）。
  - `AlreadyPresent` 视为成功。
- 0x0123 请求 `[server:u8][name:DSTR]`。应答：成功 `[1]`；失败一律 `[0][0x15]`（AM-1）。
- NOTI 0x0111：
  - subcmd 0：`[0:u32][count:u32]`，后接 count 条 `[1:u8][ch:u16][name:DSTR 1–255][level:u8][job:u32][grow:u8][bl:u8=0][tail:u32=0]`。
  - subcmd 2：`[2:u32][count:u32]`，后接 count 条 `[1:u8][name]`。
  - **ch（AM-2）**：好友在线时为其所在频道号（`ISelectedSessionDomainService.TryGetInWorld` 取得 `ListenerPort`，再经 `IChannelEntryDomainService.ResolveAsync(...).ChannelId`），离线为 0。同频道在线也写实际频道号，不为显示在线补发 USERINFO。
  - 同一份列表不得有重复名字（主键已保证 target 唯一）；名字为空或超过 255 字节的条目跳过。
  - level、grow 按 u8 写入（等级上限远小于 255）。
- NOTI 0x0112：`[ch:u16][1:u8][name:DSTR][bl:u8]`。本单元只提供消息与编码，由 TASK-06 使用。

**共享好友帧**（`Game/FriendFrames.cs`）
- 消息：
  - `FriendListMessage(entries)`：subcmd 0；
  - `FriendRemovedMessage(names)`：subcmd 2；
  - `FriendPresenceMessage(ch, name, blacklisted)`：0x0112。
- 编码器各一个，在 UnitedFriend 切片注册里登记一次（做法同 TASK-03 的共享黑名单帧）。
- 构建：`FriendListBuilder.BuildAsync(owner, relations, identities, sessions, channels, ct)` 返回 `FriendListMessage`，只读，可在 CMD 的只读单元或 NOTI 处理器里调用。构造依赖只能用 `*DomainService` 接口，使 TASK-06 能在 NOTI 处理器里复用。
- 「全量之后补发黑名单」（RQ-036；AM-1 证实全量会清掉黑名单显示）提供一个共用的小函数：给同一接收者依次登记 `FriendListMessage` 与 `BlacklistListBuilder` 的 `Response`。时区由调用方传入。

**CMD**（切片 `Game/UnitedFriend/`）
- 发起者经 `sessions.FindByConnection` 加 `IsInWorld` 确认；不在世界中时回失败（0x0122 回 0x01，0x0123 回 0x15）。
- 名字解析：在只读单元里调 `ICharacterIdentityQueryDomainService.FindByNameAsync`。
- 写入：在写单元里调 `ICharacterRelationDomainService.AddAsync` 或 `RemoveAsync`，kind 为 Friend。
- 成功后在同一作业里，按顺序向发起者登记：
  - 加好友：ACK，全量列表，黑名单列表；
  - 删好友：ACK，subcmd 2（目标名），全量列表，黑名单列表。
  - 列表在写单元提交之后，用只读单元构建。
- 目标不收任何消息。

**登录真实列表**（RQ-038）
- 用真实全量列表替换 `CrossServerFriendBaselineMessage`，并紧跟着发黑名单列表。
- 首选：保持 0x0111 在进入序列中的原位置。由选角 CMD 在已有的读取阶段构建列表，作为输入交给消息工厂，工厂里的 `new CrossServerFriendBaselineMessage()` 换成传入的列表消息，黑名单列表紧随其后。
- 如果工厂的同步形态或进入序列测试使「原位置」代价过大，可以把真实列表移到进入完成之后登记，并删掉基线帧。选这条路时，要在 Result 里说明原因，并同步修改受影响的序列断言。
- 无论哪种方案，进入序列里只有一份 0x0111 全量。

**测试**（测试先行）：字节级测试与命令测试见 Required checks。夹具参照 TASK-03 的 `BlacklistCommandTests`（ShopFixture）。

**文档**
- 协议页人工区写实现小节；`NOTI-0x0111.md` 的 A12 区按迁移规则整区删除。
- catalog 更新对应行，并写明最大 body 长度：64 条时 0x0111 subcmd 0 为 `8 + 64 × 273`。

## Result

- Outcome: done
- Round: 1，由 worker 实现；owned change 为当前未提交 diff（TASK-06.md 除外）。
- Changed:
  - 共享文件 `Game/FriendFrames.cs`：
    - 消息：`FriendListEntry`、`FriendListMessage`（subcmd 0）、`FriendRemovedMessage`（subcmd 2）、`FriendPresenceMessage`（0x0112）及其编码器；
    - 构建：`FriendListBuilder.BuildAsync`，返回 `FriendListing(Message, Skipped)`；
    - 刷新：`FriendListRefresh`（全量列表 + 0x0078，带 `Messages` 和 `RegisterAsync`）；
    - 常量 `FriendServerGroup.Own`。
  - 切片 `Game/UnitedFriend/`：Messages、PayloadCodec、两个 CMD 与薄 handler、Registration。
  - `NetworkMessages.cs`：新增 0x0122/0x0123 的客户端与服务端成员，以及 NOTI 0x0111、0x0112。
  - 选角进入：
    - `CrossServerFriendBaselineMessage` 换成 `FriendListStateMessage` + `BlacklistListStateMessage`，0x0111 留在原位，0x0078 紧随其后；
    - 工厂签名必须传入 `FriendListRefresh`；
    - 选角 CMD 增加只读单元 `ReadSelectedCharacterRelations`。
  - 越界修改：
    - `Networking/Outbound/OutboundLimits.cs`：`SelectionOutboundBound` 从 454_235 改为 474_092；
    - `OutboundQueueLimitTests`；
    - `protocol/patterns/world-entry.md`：补一段入场批次的差异说明。
  - 测试：
    - 新增 `Protocol.Tests/Game/UnitedFriend/` 三个文件、`Application.Tests/FriendCommands/FriendCommandTests.cs`（18 例）、`SelectCharacterLifecycleTests.Friends.cs`；
    - 机械修改：10 个工厂调用点；
    - 选角序列断言：62 帧改为 63 帧；
    - golden：22 份 `.hex` 在 0x0111 之后插入 `01 78` 帧。
  - 文档：`0x0122`/`0x0123`/`NOTI-0x0111`（A12 区整区删除）/`NOTI-0x0112` 的实现小节，以及 catalog 的好友小节与普查表。
- Why:
  - 0x0111 保留在进入序列的原位置：工厂只是多一个输入，没有好友时帧仍是原来的 8 字节。
  - 构建器还会跳过两类条目，并报告给调用方：与前面重名的条目（客户端会从重复项起丢弃整表），以及等级或转职超出一字节的条目。
  - 选角出站上限按新增帧重新推导，两倍仍小于 1 MiB 的队列上限。
- Checks:
  - build：0 警告，0 错误。
  - worker 无 PVF 时：Protocol 2215、Application 5983、Host 230、Networking 189，全部通过。
  - 主代理带 `PVF_ARCHIVE_PATH`（主干 `pvf-merged-20261111`）重跑：
    - Networking 218/218，0 跳过，含 golden 选角特征测试；
    - Protocol 2280/2280；
    - Application 6681/6681。
  - 行为 RED：选角测试在临时改为传 `None` 时失败。
- Remaining:
  - 频道目录里没有配置的监听端口，好友在线时 ch=0，客户端会显示离线。已记入 NOTI-0x0111 页。
  - 入场批次里未经请求的 CMD 0x0078 与 AM-2 的显示，留待实机验证。
- Quick review: approved (independent context).
  - N1: server≠1 → 0x07 confirmed by Spec AM-3, committed before this Task.
  - N2: catalog decoder census order fixed.
  - N3: the builder relies on the domain limit of 64 and does not truncate; accepted.
  - R1: the 0x0078 CMD inside a notification composition is still unproven and is left for TASK-06 to cover.
