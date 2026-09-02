# TASK-06 — 翻到的装备进背包，客户端看得见

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

新建背包变化告知（NOTI 0x000E）这个纯出站切片——**本仓完全不存在，是新协议**；
并把翻到的装备交给既有的背包到货路径，在同一次交换里发出告知。

- Referenced spec requirements: RQ-005
- Referenced plan sections: `plan.md` 的 T-006；边界 BD-011, BD-012, BD-016, BD-018, BD-019, BD-021
- Owned boundary: BD-011, BD-012, BD-016, BD-018, BD-019, BD-021
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`（增一个目录成员）
- `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs`（增类型号常量）
- `Server/src/Dfo.Server.Protocol/Game/InventoryUpdate/`（新建纯出站切片）
- `Server/src/Dfo.Server.Protocol/Game/CardReward/`（命令交付装备）
- `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`（注册行）
- `protocol/game/server-to-client/NOTI-0x000E.md`、`protocol/catalog.md`
- `Server/tests/`（新增判据）

- Dependencies: TASK-04
- Required dependency outputs: TASK-04 的翻牌交换与命令
- Acceptance conditions:

1. 告知帧按协议记录中可得的布局产出。条目区随列表类型变化而无法确定的部分，
   **记为未查明而不是猜一个**；本任务只需闭合装备这一类。
2. 若装备这一类也无法闭合，**返回阻塞报告**，不发一个形状可疑的帧。
3. 翻牌命令按仓库约定为 Transactional（它触达持久写）。
4. 顺序照地面拾取的先例：先应答后通知，同一次交换发出。
5. 协议记录的实现映射已更新并登记目录。
6. 全量判据 不低于 1690 通过、0 失败、2 跳过。

- Task-specific authorization: 根契约（revision 2）的授权覆盖本任务，无收窄。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：判据先行，再让它变绿。
- 分层按 `AGENTS.md` 与 `Docs/新功能接入规范.md`：内容源只被规则服务注入；
  规则服务不持有仓储、注册表、目录、存储或领域服务；handler 只做四类动作；
  命令处理器禁止嵌套 dispatch；能触达持久写的命令一律 Transactional。
- 归档读法在 XML doc remarks 里写明段落出处；读不出来的记为未查明，不填看似合理的数。
- `System.*` using 在前，本仓不做字母序。`TreatWarningsAsErrors`。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-014 | `dotnet test Server/86JP.Server.slnx --nologo --filter InventoryUpdate` | 告知帧的逐字节判据通过 |
| CK-015 | `Non-command:` 端到端翻到一件装备 | 该装备出现在背包中，且告知帧随应答一同发出 |
| CK-S | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 1690 通过、0 失败、2 跳过 |

- Required evidence: CK-014 的判据输出；CK-015 的端到端观察；协议记录与目录对照；全量判据输出。

## Contract Amendment 1

**触发**：执行 TASK-01 期间的独立核查发现，本 Task 密封契约的前提有一处事实错误。

**错在哪。** 契约与计划都写着「背包变化告知 NOTI 0x000E 本仓完全不存在，是要新建的出站
协议」，并把它列为本任务最大的未预料工作量。**这是错的。** 该帧早已实现：

- `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs:60`
  的 `InventoryDeltaMessage`，`internal override ushort Type => 0x000E`。
- 编码器 `StateSynchronizationPayloadCodec.Inventory.cs:183` 的 `EncodeInventoryDelta`：
  `u8 容器类型 + u16 条目数 + 条目区`，条目写法 `WriteInventoryItem` 与全量快照共用。
- 已注册为 NOTI（同文件 :588 的 `AddNoti<InventoryDeltaMessage>`）。
- 已有生产调用方：`SelectCharacter/SelectCharacterHandler.cs:253-263`，入场时发当日补给的增量。
- 协议记录 `protocol/game/server-to-client/NOTI-0x000E.md` 的 A21 一节已把布局解闭合
  （`3 + 5×101 = 508`），并记录了本仓撤掉空信封的那次改动。

**为什么先前的普查没看见。** 它按上游命名搜（`UpdateItemList`、`InventoryRefreshSender`）
并在 `ServerMessageType` 枚举里找 `0x000E`，而本仓管它叫 `InventoryDeltaMessage`，
且 StateSynchronization 切片把类型号放在消息记录自身而不是目录枚举里。两头都搜不到。

**因此本任务的实际工作变小，但换了一个性质。** 不再是新建出站协议，而是：
`InventoryDeltaMessage` 是 StateSynchronization 切片内的 `internal` 类型，
`AGENTS.md` 的「切片内不跨协议引用；要共用就上提到 endpoint 层或领域服务」禁止翻牌切片
直接引用它——这正是上一个重构刚清掉的四处越界的同一形状。共用必须先上提。

**修订后的 Goal and deliverables**：把背包增量告知从 StateSynchronization 切片上提到
可共用的位置（endpoint 层或领域服务，二选一由执行者按 `AGENTS.md` 判断并写明理由），
使翻牌到货与入场补给共用同一条投递，然后把翻到的装备交给既有的背包到货路径并在同一次
交换里发出告知。

**修订后的 Permitted files or interfaces**：
- `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/`（上提，不改其字节输出）
- `Server/src/Dfo.Server.Protocol/Game/`（endpoint 层的共用落点，若选这条路）
- `Server/src/Dfo.Server.Protocol/Game/CardReward/`（命令交付装备并发出告知）
- `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs`（改为经上提后的路径）
- `Server/tests/`（新增判据）
**删去**：`Game/InventoryUpdate/` 新切片、`NetworkMessages.cs` 与 `GameProtocol.cs` 的新目录成员、
`ProtocolModule.cs` 的新注册行——都不再需要，该帧已经接好线了。

**修订后的 Acceptance conditions**：
1. 翻牌到货与入场补给走同一条增量告知，翻牌切片**不直接引用** StateSynchronization 切片的类型。
2. **入场路径的字节输出逐字节不变**——上提是搬家不是改写。既有的入场判据必须原样通过。
3. 翻牌命令按仓库约定为 Transactional（它触达持久写）。
4. 顺序照地面拾取的先例：先应答后通知，同一次交换发出。
5. 协议记录 `NOTI-0x000E.md` 的人工区补记新增的第二个调用方。
6. 全量判据不低于 1690 通过、0 失败、2 跳过。

**修订后的 Required checks**：CK-014 改为
`dotnet test Server/86JP.Server.slnx --nologo --filter StateSynchronization`
（断言上提后入场路径的字节输出不变）；CK-015 与 CK-S 不变。

**原契约作废的部分仅限上述。** Spec 的 RQ-005（装备进背包且客户端被告知）未变，
本 Task 的验收边界未变，故按 `task-rework` 的口径就地修订，不新建替代 Task。

## Execution Result — round 1

### 改了哪些文件

**新增（2 个）**

- `Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs` — 端点层落点。内含四样东西：
  条目写法 `GameInventoryItemEntry`（原 `WriteInventoryItem` + 两个长度常量 + `PopulatedCount`）、
  消息 `InventoryDeltaMessage`、编码器 `InventoryDeltaEncoder`、共用投递 `GameInventoryDelivery`。
- `Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/InventoryDeltaLiftTests.cs` — CK-014 的判据。

**改动（源码 6 个）**

- `.../Game/StateSynchronization/StateSynchronizationMessages.cs`：删去 `InventoryDeltaMessage` 记录，
  删去 `AddStateSynchronizationEncoders` 里那行 `AddNoti<InventoryDeltaMessage>`。
- `.../Game/StateSynchronization/StateSynchronizationPayloadCodec.Inventory.cs`：删去 `EncodeInventoryDelta`、
  `WriteInventoryItem`、`PopulatedCount`、`CommonItemEntryLength`、`WrittenItemEntryLength`；
  全量快照的三处调用改指 `GameInventoryItemEntry.Write` / `.CommonLength`；`Missing` 留在原处
  （它被同一 partial 的 `.Entry.cs` 大量使用，不属于本次上提的东西）。
- `.../Game/GameMessageSemantics.cs`：`AddGameMessageSending()` 里加一行
  `GameInventoryDelivery.AddEncoders(services)`。理由写在注释里：接线必须落在端点层自己的入口，
  留在切片的注册表里等于那个切片仍然拥有它。
- `.../Game/SelectCharacter/SelectCharacterHandler.cs`：`inventory.CommittedUpdates.Select(...)` 那段
  改成 `GameInventoryDelivery.Notifications(inventory.CommittedUpdates)`。净变化 1 增 3 删。
- `.../Game/CardReward/SelectCardCommand.cs`：`SelectCardResult` 加 `CommittedInventoryUpdateState? Delivered`；
  命令处理器加 `ICharacterContainerDomainService` 与 `TimeProvider` 两个依赖，新增私有 `DeliverAsync`。
- `.../Game/CardReward/CardRewardHandlers.cs`：`SendCmdAsync` 改成一次 `SendExplicitAsync`，
  应答在前、告知在后。
- `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`：`SelectCardCommand` 的执行种类
  `Workflow` → `Transactional`，并改写它上面那段注释（该注释原本就预言了这次改动）。
  **这一处涉及边界判断，见下面「拿不准的地方」第 1 条。**

**改动（判据 4 个）**

- `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs`：`World` 夹具加背包与固定时钟，
  新增 12 条判据（到货、耐久、顺序、告知内容、三种拒绝），并把接线判据从「注册过」加强为
  「注册为 `Transactional`」。
- `Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs`：夹具加
  `refillCommits` 开关（默认 false，既有判据的 ledger 一字未动），新增 1 条判据钉住入场路径
  仍在补给之后、尾帧之前发出这一帧。
- `.../Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`：辅助方法的形参由
  `IReadOnlyList<GameStateSynchronizationMessage>` 放宽为 `IReadOnlyList<IGameServerMessage>`（1 增 1 删）。
  **断言与那条 SHA-256 一个字都没改。**
- `.../Game/SelectCharacter/SupplementalSynchronizationTests.cs`：`GameNotiStateSynchronizationEncoder<InventoryDeltaMessage>`
  换成 `InventoryDeltaEncoder`（1 增 1 删）。断言未改。
- `.../Game/StateSynchronization/ContainerInventoryProjectionTests.cs`：多一行 `using`（1 增 0 删）。断言未改。

**协议记录 2 个**

- `protocol/game/server-to-client/NOTI-0x000E.md`：人工区新增一节「2026-09-01：第二个调用方——通关翻牌」。
- `protocol/catalog.md`：0x000E 那一行的说明由「发当日补给的真实增量」改为「两个调用方……」。

### 上提到了哪里，为什么选那里

**选端点层 `Server/src/Dfo.Server.Protocol/Game/`，不是领域服务。** 理由是被共用的东西是什么：
`InventoryDeltaMessage` 是一个出站帧、它的线级类型号、它的编码器，以及与全量快照共用的 101 字节
条目写法——全是协议知识。`AGENTS.md` 的分层表里 `Dfo.Server.Protocol` 才是放消息、编码器的地方，
领域服务在 Application/Abstractions，把一个帧塞进去等于把协议知识搬出协议层。
端点层已有同形状的先例，且先例自己写明了理由：`GameProtocol.SecretShopNpcId` 的注释说
「两个切片都要用它，它在这里而不在任何一个切片里，因为切片不能伸手进另一个」，
`GameDropIdentity.cs`、`GameCmdEnvelope.cs` 同理。

上提的**范围**是三样，缺一不可：

1. 消息记录本身（翻牌切片要 `new` 它）；
2. 它的编码器与注册（否则注册仍在被上提的切片里，等于没搬）；
3. 条目写法 `GameInventoryItemEntry`（`EncodeInventoryDelta` 依赖它，而全量快照也依赖它。
   若把它留在切片里由端点层反向调用，方向就倒了——端点层依赖切片。所以它一并上提，
   由切片正向调用）。

`ServerMessageType.GameStateSynchronization` **保留没动**。那个目录成员是整个状态同步族的桶
（族把线级类型号放在消息记录自身，不放目录），不是某一个协议的名字；而且 Amendment 已把
`NetworkMessages.cs` 的新目录成员从许可清单里删掉了。

**「同一条投递」是结构性的，不是巧合。** 两边都调 `GameInventoryDelivery.Notification(update)`，
入参是同一个 `CommittedInventoryUpdateState`；翻牌命令因此也产出一个 `CommittedInventoryUpdateState`，
而不是自己拼消息。判据 `A_batch_of_committed_changes_becomes_one_frame_each_in_order` 钉住这一点。

### 入场路径字节不变怎么验的

**硬证据是一条上提之前就已在案的整帧 SHA-256。**
`SelectCharacterAcceptedTranscriptTests` 里那条
`F9BE81C62166A8D13FDD70545AAC31351973CFCF6A84A0421BBFAF9E0E8A27FC`
是这次改动之前就写在仓库里的值（该测试文件本轮只改了一个辅助方法的形参类型，断言未动）。
我把同一个值**抄进** `InventoryDeltaLiftTests.The_entry_paths_delta_frame_is_unchanged_by_the_lift`，
让它落进 CK-014 的 `--filter StateSynchronization` 覆盖范围里，并且走的是生产图自己的注册
（`new ProtocolModule().ConfigureServices(...)` → 按 CLR 类型找到唯一注册 → 用注册的编码器编码），
所以「搬了类型但丢了接线」也会红。它覆盖 15 字节帧头与 body 全部。

一条哈希不够，所以另加三类：

- 条目区与全量快照的对应条目**逐字节相同**，用两个各处不同的物品（槽位、id、数量、耐久都不同），
  并额外断言这两条条目**彼此不同**——否则「写死一个常量块」也能过。这是 TASK-03 那个
  「单样本赌巧合」的教训。
- body 闭合于 `3 + count×101`，count 取 0、1、2 三个值。
- 列表类型字节跟着容器走（Main / PersonalCargo 两个值），不是写死的。

另外两条既有判据原样通过：`ContainerInventoryProjectionTests` 的两条（只多了一行 `using`），
以及 `SupplementalSynchronizationTests` 里对 0x000E 的类型号与前三字节的断言（只换了编码器类名）。
入场路径**仍然发出**这一帧，由新增的 `SelectCharacterLifecycleTests.CommittedRefill_SendsTheBagChangeBetweenTheRefillAndTheTail`
钉住（见变异 M9）。

### 判据真实输出

**CK-S 全量**
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

```text
Networking      51 通过 / 0 失败 / 0 跳过
Protocol       462 通过 / 0 失败 / 0 跳过
Pvf             60 通过 / 0 失败 / 0 跳过
Persistence    244 通过 / 0 失败 / 0 跳过
Application    848 通过 / 0 失败 / 0 跳过
Host             4 通过 / 0 失败 / 0 跳过
Infrastructure 204 通过 / 0 失败 / 2 跳过
合计          1873 通过 / 0 失败 / 2 跳过
```

改动前基线 1852 / 0 / 2，净增 21 条（本轮新增判据数正好 21）。一次跑完，没有遇到简报里说的
两种假红。

**CK-014**（Amendment 修订后的形式）
`dotnet test Server/86JP.Server.slnx --nologo --filter StateSynchronization`

```text
失败: 0，通过: 11，已跳过: 0，总计: 11 - Dfo.Server.Protocol.Tests.dll
```

11 条的来源：新增的 `InventoryDeltaLiftTests` 8 条，既有的 `ContainerInventoryProjectionTests` 2 条，
以及名字里含 `StateSynchronization` 的
`SelectCharacterAcceptedTranscriptTests.ConcreteStateSynchronizationMessages_EachHaveExactlyOneGameEncoderRegistration` 1 条。
改动前这个 filter 只匹配到后 3 条。

**CK-015 端到端**（非命令项，落成判据跑）
`dotnet test Server/86JP.Server.slnx --nologo --filter CardReward`

```text
失败: 0，通过: 60，已跳过: 0，总计: 60 - Dfo.Server.Protocol.Tests.dll
失败: 0，通过: 12，已跳过: 0，总计: 12 - Dfo.Server.Application.Tests.dll
```

其中直接对应 CK-015 的是：`A_flipped_piece_is_in_the_bag_afterwards`（两个牌位，两件不同的装备）、
`The_answer_comes_first_and_the_bag_change_after_it_in_one_exchange`、
`The_bag_change_carries_the_slot_the_piece_and_how_many`（读的是编码后的 body：
选择子 `0x00`、类型 `0x000E`、列表类型、条目数、槽位、物品 id、数量、耐久）。

### 变异检查结果

方法：改一处实现 → 跑对应 filter → 还原。12 个变异，**全部变红**：

| # | 变异 | 结果 |
|---|---|---|
| M1 | `GameInventoryItemEntry.CommonLength` 101 → 100 | `StateSynchronization` 4 失败 / 7 通过 |
| M2 | 条目里的耐久字段写死 0 | `StateSynchronization` 1 失败 / 10 通过 |
| M3 | 线级类型号 `0x000E` → `0x000F` | `StateSynchronization\|CardReward` 3 失败 / 68 通过 |
| M4 | 翻牌到货不带过耐久 | `CardReward` 3 失败 / 57 通过 |
| M5 | 告知帧里的槽位写死 0 | `CardReward` 2 失败 / 58 通过 |
| M6 | 翻到装备也不交给背包 | `CardReward` 8 失败 / 52 通过 |
| M7 | 告知在前、应答在后 | `CardReward` 4 失败 / 56 通过 |
| M8 | 应答与告知拆成两次交换（内容顺序都不变） | `CardReward` 1 失败 / 59 通过 |
| M9 | 入场路径不再发这一帧 | `SelectCharacterLifecycleTests` 1 失败 / 18 通过 |
| M10 | 共用投递漏掉一条更新 | `StateSynchronization` 1 失败、`SelectCharacterLifecycle` 1 失败 |
| M11 | 背包放不下时也发告知 | `CardReward` 2 失败 / 58 通过 |
| M12 | 翻牌命令改回 `Workflow` | `CardReward` 1 失败 / 59 通过 |

M2 只红 1 条是**预期**的：条目区与全量快照的对应关系判据两边用同一个写法，改写法两边一起变，
所以钉住内容的是那条整帧哈希——这正是它存在的理由，也写进了该判据的注释里。

M8（拆成两次交换、顺序与内容都不变）能红，是因为 `RecordingSender` 除了 `Sent` 还记 `Exchanges`；
只看 `Sent` 的话这个变异是活的。

**一次工具事故，已修正并复跑。** 我的还原脚本用 `shutil.move` 把备份挪回原处，
被挪回的文件带的是**备份时刻的 mtime**，比它之间那次编译更早，于是 MSBuild 认为它没变、
**沿用了带变异的程序集**。第一轮 M9 因此得出「不会红」的错误结论，我据此差点把一个真实的
判据缺口写成「无法弥补」。发现后给还原脚本加了 `os.utime`，把全部 12 个变异**从头重跑**，
上表是重跑的结果。上面所有判据数字也是在 touch 全部改动文件、重新编译之后跑出来的。

### 行尾按什么方法核的

按字节数，不用 `grep -c $'\r'`：对每个改动/新增文件读二进制，比 `b.count(b'\r\n')` 与
`b.count(b'\n')`。64 个文件里唯一带 CRLF 的是
`.rks-agent/.../tasks/TASK-02-card-face-rule-service.md`（777 CRLF / 1327 LF）——**那是 TASK-02 的产物，
本轮没碰**。我新增与改动的文件全部 0 CRLF。
另外 `git diff --numstat` 与 `git diff --ignore-cr-at-eol --numstat` 输出**逐行完全一致**
（27 行，diff 无输出）。

### 意外与处理

1. **参照服务端的报文日志里有翻牌之后的 `0x000E`，我先写错了又改对。**
   写协议记录时我先写了「参照会话里没有翻牌到货的样本，因此参照是否用本帧告知翻牌所得无从判断」。
   随后去核，发现 `E:/Project/DNF/ServerS4A21/_reference-backup-20260826/packet_log.txt`
   （本仓之外的参照服务端备份）822–825 行**正好是这一对帧**：
   `SEND cmd=0x01 type=0x0047` 紧跟 `SEND cmd=0x00 type=0x000E len=119 body=(104B)`，中间没有别的帧。
   我的断言是错的，已删并换成这份判据。这条日志坐实的是：
   **参照在翻牌应答之后、中间不插别的帧，紧接着发了这一帧**，
   以及 body 闭合于 `3 + 1×101 = 104`。

   **（QR-06-1 更正）** 此处原写「同一次交换」，超出了证据：日志是逐帧记 `SEND` 的，
   它能坐实顺序与相邻，坐实不了两帧是否走同一次投递。
   「同一次交换」这一点本 Task 依据的是仓内地面拾取的先例，不是这份日志。
   同一份日志 148 行是参照自己的 `0x000D` 主背包快照（14 条），头三条是
   `槽 0 / id 0`、`槽 1 / id 1`、`槽 2 / id 2`——由此可知那一帧翻牌后的 `0x000E` 里
   `槽 0 / id 0 / 数量 145` 是**虚拟槽**，不是一件装备。所以
   「参照如何用本帧描述一件装备的到货」仍然没有样本，这一点在协议记录里记为未查明。
   （虚拟槽 0 变成 145 像是翻牌给的钱，那是 T-007 的事，本轮不碰。）
2. **背包写完之后不能用 `SnapshotAsync` 建这一帧。** `PersistentItemContainer.SnapshotAsync`
   走的是入场投影（读表），而耐久那一次 `PutAsync` 的写在用例边界结算前只存在于 store session 里
   （`AddAsync` 内部的 `ApplyDecidedWritesAsync` 是每个动词一次，我这次额外的 `PutAsync` 之后没有）。
   用它建帧会把「刚开的槽、耐久 0」告诉客户端，而库里最终是带耐久的。改成用 `ReadSlotAsync`
   读回（它读 session，看得见未结算的写）并用刚写下的 core 建条目。
   `InventoryItemState.StorageEntryId` 因此填 0——那是持久化身份，本帧没有这个字段，
   协议层只在一处 avatar 的报错文案里提到它；这一点在代码注释里写明了。
3. **翻牌命令必须是 `Transactional`，否则这条路根本跑不起来。** 不是记账问题：
   `PersistentItemContainer.WriteAsync` 在没有边界时**直接抛** `InvalidOperationException`
   （`ItemContainers.cs` 里那段「keeps its items past this process, so it is only changed inside a use case」）。
   `UnitOfWorkBehavior` 只对 `Transactional` 开边界。所以这一行是功能性的。
4. **耐久只在「开了新槽」时带过，加入已有堆时不带。** 照地面拾取的 `CarryOverAsync`：
   堆自己已经有耐久，覆盖它等于改写背包里本来就有的东西。判据两边都钉了。

### 拿不准的地方

1. **`ProtocolModule.cs` 是否在许可清单内——我按「在」处理了，请控制器复核。**
   Amendment 的「修订后的 Permitted files」五条里没有 `ProtocolModule.cs`，
   但它的「删去」一条写的是「`ProtocolModule.cs` 的**新注册行**」，理由是「该帧已经接好线了」——
   删的是那个不再存在的新切片的编码器注册行。
   两种读法：
   - 读法 A（五条清单是完整替换）：`ProtocolModule.cs` 与 `protocol/` 全部出界。
     但修订后的验收条件 5 要求改 `NOTI-0x000E.md`，条件 3 要求命令是 `Transactional`，
     **两者在读法 A 下都无处落笔**——清单自相矛盾。
   - 读法 B（清单只替换它点名的部分，原清单其余项存续）：`protocol/game/server-to-client/NOTI-0x000E.md`、
     `protocol/catalog.md` 存续（条件 5 需要），`ProtocolModule.cs` 的**既有**注册行存续
     （条件 3 需要，且我改的是既有行不是新行）。
   只有读法 B 自洽，我按 B 做了：改了 `ProtocolModule.cs` 一个枚举值加一段注释，
   改了两个 `protocol/` 文件。**如果控制器认定读法 A，这三处请直接回退**——
   `ProtocolModule.cs` 回退会让验收条件 3 落空、且翻牌到货在生产上会抛异常（见「意外」第 3 条），
   届时需要一次契约修订而不是我这边的改法调整。我没有单独提 Blocker Report，
   因为按读法 B 我并没有停在任何地方；这一条就是把判断摆出来。
2. **`InventoryDeltaMessage` 仍报 `ServerMessageType.GameStateSynchronization`。**
   我认为这是对的（那是族的桶不是协议名，且不许新增目录成员），但它读起来确实像
   「上提了却还挂着原切片的名字」。若控制器要一个中性的目录成员，那需要放开 `NetworkMessages.cs`。
3. **参照是不是每次翻牌都发 `0x000E`**，还是只在确有变化时才发——只有一份样本，无从判断。
   本仓的选择是「没东西到货就不发」，与入场那条「`CommittedUpdates` 为空就一帧不发」一致。
   已记进协议记录的未查明项。
4. **翻牌到货把装备放进主背包（`InventoryContainerType.Main`）** 是本仓的选择；
   参照那一帧的列表类型也是 `0x00`（主背包），但那条样本装的是虚拟槽，不是装备到货，
   所以它只能说明「同一个列表」，不能说明「参照把牌上的装备放主背包」。

## Executor Self-check — round 1

逐条对照 `Contract Amendment 1` 的「修订后的 Acceptance conditions」：

**1. 翻牌到货与入场补给走同一条增量告知，翻牌切片不直接引用 StateSynchronization 切片的类型。**
达成。两边都经 `GameInventoryDelivery.Notification(CommittedInventoryUpdateState)`，
翻牌命令产出的也是 `CommittedInventoryUpdateState`。
`Server/src/Dfo.Server.Protocol/Game/CardReward/` 下没有任何 `Dfo.Server.Protocol.Game.StateSynchronization`
的 `using` 或类型引用（`CardRewardHandlers.cs` 的 `using` 是 `Dfo.Server.Protocol.Game`，端点层）。
判据 `The_frame_that_tells_the_client_lives_in_the_endpoint_layer` 断言送出的帧的
`Namespace == "Dfo.Server.Protocol.Game"`——它若被搬回切片就红。

**2. 入场路径的字节输出逐字节不变；既有的入场判据原样通过。**
达成。上提前就在案的整帧 SHA-256 `F9BE81C6…` 仍然成立，且是经生产图注册的编码器算出来的；
另有条目区与快照逐字节一致（两个不同物品）、body 闭合（三个 count）、列表类型跟随容器（两个值）。
既有判据的**断言一字未改**：`SelectCharacterAcceptedTranscriptTests` 只放宽了一个辅助方法的形参类型，
`SupplementalSynchronizationTests` 只换了编码器类名，`ContainerInventoryProjectionTests` 只多一行 `using`。
变异 M1/M2/M3/M10 各自变红。

**3. 翻牌命令按仓库约定为 Transactional。**
达成，但落笔处是 `ProtocolModule.cs`——边界读法见「拿不准的地方」第 1 条。
判据：`The_card_protocols_are_wired_into_the_production_graph` 现在断言该注册的
`ExecutionKind == CommandExecutionKind.Transactional`；变异 M12 变红。
另附理由：该命令现在触达 `IItemContainer.AddAsync`/`PutAsync`，持久容器在无边界时抛异常。

**4. 顺序照地面拾取的先例：先应答后通知，同一次交换发出。**
达成。`SelectCardHandler` 一次 `SendExplicitAsync`，`RequiredDirectMessages` 为
`[SelectCardResponse, InventoryDeltaMessage]`。判据
`The_answer_comes_first_and_the_bag_change_after_it_in_one_exchange` 断言**恰好一次交换**且顺序如上；
变异 M7（换序）与 M8（拆两次）分别变红。
先例已读：`Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs` 的 `GetItemHandler`
把 `GetItemResponse` 与 `GroundPickupNotification` 放进同一个 `GameExplicitMessageDelivery`。
额外：参照服务端的报文日志 822–825 行也是这个顺序（见「意外」第 1 条）。

**5. 协议记录 `NOTI-0x000E.md` 的人工区补记新增的第二个调用方。**
达成。新增一节「2026-09-01：第二个调用方——通关翻牌」，内容包含：参照日志的两帧原文与行号、
body 闭合算式、这条样本装的是虚拟槽而非装备（附参照自己的 `0x000D` 快照为据）、
两项未查明、以及实现映射从切片到端点层的变化。`protocol/catalog.md` 那一行的说明一并更新
（该文件的许可性见「拿不准的地方」第 1 条）。

**6. 全量判据不低于 1690 通过、0 失败、2 跳过。**
达成：1873 通过 / 0 失败 / 2 跳过（简报给的当前基线 1852 / 0 / 2，净增 21）。

其余约束自查：

- **分层**：新增的端点层文件不持有仓储/注册表/目录/存储；`SelectCardCommandHandler` 新增的两个依赖是
  领域服务 `ICharacterContainerDomainService` 与 `TimeProvider`，不是仓储。
- **handler 四类动作**：`SelectCardHandler` 只做「解会话（只读目录）」「`commandBus.SendAsync`」
  「sender 发送」——没有业务语义。到货、耐久、拒绝判读全在命令处理器里。
- **命令处理器禁止嵌套 dispatch**：`SelectCardCommandHandler` 不发任何命令。
- **`System.*` using 在前，不做字母序**：新增两个文件均符合（`GameInventoryDelivery.cs` 无 `System.*` 显式 using；
  `InventoryDeltaLiftTests.cs` 以 `System.Buffers.Binary`、`System.Security.Cryptography` 开头）。
- **`TreatWarningsAsErrors`**：`dotnet build` 0 警告 0 错误。
- **改一处之前先 grep**：上提前 grep 过 `WriteInventoryItem`/`CommonItemEntryLength`/`PopulatedCount`/
  `EncodeInventoryDelta`/`InventoryDeltaMessage` 的全部出现处（src 与 tests），确认没有第二处；
  `Missing` 有多处，所以它留在原地没搬。
- **断言由判据钉住或删去**：本节与上一节里对本文件之外的可验证断言，都给了出处
  （参照日志的文件名与行号、仓库内的文件路径与符号名、判据名与变异编号）。
  唯一被我写错又删掉的断言，记在「意外」第 1 条。

## Controller Disposition — 边界歧义是控制器造成的，批准并补记

执行者在交付里显著披露：`Contract Amendment 1` 的许可清单没有列
`ProtocolModule.cs` 与 `protocol/`，而验收条件 3（命令改为 `Transactional`）
与条件 5（更新 `NOTI-0x000E.md`）只能落在这两处；它按「清单只替换它点名的部分」
这一读法做了，并请控制器复核。

**裁定：批准，且错在控制器。**

Amendment 1 原文写着「**删去**：…`ProtocolModule.cs` 的新注册行——都不再需要，
该帧已经接好线了」。那个理由**只覆盖「注册这个帧」这一件事**，
而条件 3 要求的 `Transactional` 是同一文件里**另一处**注册（命令的执行方式）。
**修订因此自相矛盾**：它一边要求条件 3，一边划掉了唯一能满足条件 3 的地方。

Permitted files or interfaces 补记：
- `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`（**仅**翻牌命令的 ExecutionKind）
- `protocol/game/server-to-client/NOTI-0x000E.md`、`protocol/catalog.md`（条件 5 所需）

**执行者的处置对不对**：它没有停下写阻塞报告，而是按一个可辩护的读法做了并显著披露。
按简报的字面（「越界之前先停下」）更严的做法是停下。但**歧义是控制器造成的**，
且披露充分、理由写明、后果分析（回退 `ProtocolModule.cs` 会让条件 3 落空、
翻牌到货在生产上抛异常）也给了。**本次不算违规。**

**给控制器自己的教训**：写「删去某某」时，理由必须覆盖那个文件在本 Task 里的**全部**用途，
不能只覆盖想到的那一个。这条与本 Spec 反复出现的形状同源——**一个只核对了一半的判断**。

## Controller Note — 变异检查本身出过一次假阴性，这条值得单独记

执行者报告：它的变异还原脚本用 `shutil.move` 复原文件，导致 **mtime 回退**，
于是 MSBuild 认为程序集是新的、沿用了**带变异的旧编译结果**——
第一轮 M9 因此显示「不会红」，被误判为一条空转判据。
加 `os.utime` 修正后重跑，**12 个变异全部转红**，交付里的数字都是重跑结果。

**这是本 Spec 第一次抓到「验证器本身坏了」。**

此前二十余处缺陷的形状都是「说得顺口但没人核」；这一处不同——**核了，工具骗了人**。
而且骗的方向最危险：**假阴性**。一次假阳性会让人去查，一次假阴性只会让人放心。

TASK-04 的评审当时做过一件正确的事：**先用合成样例证伪自己的锚点检测器**
（旧锚点必须报错、新锚点必须通过），确认它真能发现问题，然后才信它的输出。
本次是同一课的反面教材。

**立为后续任务的做法**：变异检查的还原步骤必须保证下一次构建真的重编译
（复制内容而非移动文件，或显式回填 mtime）；且**至少有一个变异必须先被观察到变红**，
才能把「其余变异也红」当成证据——一个从头到尾都没红过的变异循环，
与一个根本没跑的变异循环无法区分。

## Independent Quick Review — QR-06-1

- 裁决：**passed**
- 评审的契约版本：Contract revision 1 + `Contract Amendment 1`（验收按 Amendment 的「修订后的
  Acceptance conditions」六条），外加两条 Controller 记录（许可清单补记、变异工具事故）。
- owned diff 身份：工作区未提交，基线 `c0cb6c99`。本轮核的是 TASK-06 名下的
  `Game/GameInventoryDelivery.cs`（新增）、
  `tests/.../StateSynchronization/InventoryDeltaLiftTests.cs`（新增），
  改动 `StateSynchronizationMessages.cs`、`StateSynchronizationPayloadCodec.Inventory.cs`、
  `GameMessageSemantics.cs`、`SelectCharacterHandler.cs`、`CardReward/SelectCardCommand.cs`、
  `CardReward/CardRewardHandlers.cs`、`ProtocolModule.cs`、四个判据文件、
  `protocol/game/server-to-client/NOTI-0x000E.md`、`protocol/catalog.md`。
  TASK-01..05 的产出同在工作区但不在本轮范围。

### 我自己跑的证据

- `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
  → 51 + 462 + 60 + 244 + 848 + 4 + 204 = **1873 通过 / 0 失败 / 2 跳过**，与交付所报一致。
  两条跳过是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
  与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
  ——因为档案在才跳过，符合预期。一次跑完，未遇简报所列的两种假红。
- CK-014 `--filter StateSynchronization` → 11 通过 / 0 失败；
  CK-015 `--filter CardReward` → Protocol 60 通过、Application 12 通过，均与交付一致。
- 行尾按字节数核（`b.count(b'\r\n')` 对 `b.count(b'\n')`，未用 grep 数回车）：
  本轮 16 个源码/判据/协议文件与本 Task 文件全部 **0 CRLF**。
  `git diff --numstat` 与 `git diff --ignore-cr-at-eol --numstat` 27 行逐行相同。

### 一、入场路径的字节确实没变

- **上提是搬家。** 我把 `HEAD` 版 `WriteInventoryItem` 的函数体与新
  `GameInventoryItemEntry.Write` 的函数体做程序化比对（仅把两个常量名
  `CommonItemEntryLength`/`WrittenItemEntryLength` 换成 `CommonLength`/`WrittenLength`）：
  **逐字符相同**。`EncodeInventoryDelta` 的函数体与 `InventoryDeltaMessage.EncodeBody` 同理。
  快照侧唯一的改动是 `WriteVirtualInventoryItem` 的 `CommonItemEntryLength - 10`
  改指 `GameInventoryItemEntry.CommonLength - 10`，同为 101。
- **那条哈希确实是上提之前就在案的。**
  `git show HEAD:Server/tests/.../SelectCharacterAcceptedTranscriptTests.cs` 第 274 行即
  `F9BE81C62166A8D13FDD70545AAC31351973CFCF6A84A0421BBFAF9E0E8A27FC`，输入也相同
  （`SelectCharacterPayloadTestData.CreateComplete()` 的 `main.Items[0]`、
  `source.World.PreparedAt`），两边都经 `new ProtocolModule().ConfigureServices(...)` 后
  按 CLR 类型查到的注册编码器编码。所以「搬了类型丢了接线」会红，「改了字节」也会红。
- **既有入场判据的断言一字未改。** 逐个核过本轮对判据文件的全部改动：
  `SelectCharacterAcceptedTranscriptTests.cs` 只有辅助方法形参
  `IReadOnlyList<GameStateSynchronizationMessage>` → `IReadOnlyList<IGameServerMessage>`
  （1 增 1 删，第 405 行），**那条 SHA-256 断言与它下面的条目区比对原样保留**；
  `SupplementalSynchronizationTests.cs` 只换编码器类名（第 57 行），
  `0x000E` 类型号与前三字节的断言未动；
  `ContainerInventoryProjectionTests.cs` 只多一行 `using`。**没有放宽、删减或弱化。**
- 附带一处覆盖迁移：`InventoryDeltaMessage` 不再是 `GameStateSynchronizationMessage`，
  因此不再落入 `ConcreteStateSynchronizationMessages_EachHaveExactlyOneGameEncoderRegistration`
  的枚举范围；`InventoryDeltaLiftTests.EncodeThroughRegisteredEncoders` 里的 `Assert.Single`
  对同一件事做了等价断言，覆盖没有净损失。

### 二、上提到端点层是恰当的

`AGENTS.md:233-234` 原文：「**切片内不跨协议引用**。要共用就上提到 endpoint 层
（`Channel/ChannelProtocol.cs`、`Game/GameProtocol.cs`）或领域服务。」二选一里领域服务这条
被 `AGENTS.md:35` 的分层表挡住——消息、编码器属于 `Dfo.Server.Protocol`，领域服务在
Application，把出站帧与编码器塞进去等于把协议知识搬出协议层。选端点层正确。
同目录已有同形状先例 `GameProtocol.cs`、`GameDropIdentity.cs`、`GameCmdEnvelope.cs`、
`CharacterNameField.cs`，另起一个文件而非塞进 `GameProtocol.cs` 与这些先例一致。
上提范围完整：`grep` 全仓确认 `Server/src/.../Game/CardReward/` 下**零个**对
`Dfo.Server.Protocol.Game.StateSynchronization` 的引用；编码器注册也随之落在端点层自己的
入口（`GameMessageSemantics.cs:126`，全仓唯一调用点 `ProtocolModule.cs:81`）。

### 三、Transactional 与发送顺序

- `ProtocolModule.cs` 里 `SelectCardCommand` 注册为
  `CommandExecutionKind.Transactional`。**这一行是功能性的**，不只是记账：
  `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:604-610`
  的 `PersistentItemContainer.WriteAsync` 在没有环境事务时直接抛
  `InvalidOperationException`。与之比照的地面拾取 `PickUpGroundDropCommand`
  （`ProtocolModule.cs:182-186`）同为 `Transactional`，注释理由同源。
- **先例我自己读了**：`GroundPickupSlice.cs:474-485`，一次 `SendExplicitAsync`，
  `[GetItemResponse, GroundPickupNotification]`。翻牌侧
  `CardRewardHandlers.cs:113-124` 形状完全相同，`[SelectCardResponse, InventoryDeltaMessage]`。
  判据 `The_answer_comes_first_and_the_bag_change_after_it_in_one_exchange` 断言的是
  `Assert.Single(sender.Exchanges)` 而不是 `Sent` 的条数——这正是能把 M8 钉住的地方。

### 四、变异检查可信

我没有复跑变异（本轮不改产品代码），改为把 12 个变异逐条与判据文本对账，
**看每个变异会红在哪一条断言、红几条**：

| # | 会红的具体断言 | 应红条数 | 交付所报 |
|---|---|---|---|
| M1 | 整帧 SHA、`The_delta_writes_an_item…`、body 闭合 theory 的 count=1/2 | 4 | 4 ✓ |
| M2 | 整帧 SHA（fixture 的 `Durability=50`，非零，故必红） | 1 | 1 ✓ |
| M3 | 整帧 SHA、`The_bag_change_carries…` 两个 theory 例 | 3 | 3 ✓ |
| M4 | `A_piece_that_opened_a_slot_carries…`、`The_bag_change_carries…` ×2 | 3 | 3 ✓ |
| M5 | `The_bag_change_carries…` ×2（`Bag.OpenedSlot = 12`，非零，故必红） | 2 | 2 ✓ |
| M6 | 到货 ×2、耐久 ×2、顺序、端点层归属、告知内容 ×2 | 8 | 8 ✓ |
| M7 | 顺序、端点层归属（`Sent[1]` 变成应答）、告知内容 ×2 | 4 | 4 ✓ |
| M8 | 仅 `Assert.Single(sender.Exchanges)` 那一条 | 1 | 1 ✓ |
| M9 | `CommittedRefill_SendsTheBagChangeBetweenTheRefillAndTheTail` 的 ledger 少 `delta` | 1 | 1 ✓ |
| M10 | `A_batch_of_committed_changes…` 序列长度不等 + 上面那条 ledger | 1+1 | 1+1 ✓ |
| M11 | `A_bag_that_could_not_take…` 的 NoRoom / UnknownItem 两例（第三例无容器，不受影响） | 2 | 2 ✓ |
| M12 | `The_card_protocols_are_wired…` 的 `ExecutionKind` 断言 | 1 | 1 ✓ |

12 条**逐条对上，一条不差**。这本身就是重跑属实的强证据：假阴性那次的症状是「0 失败」，
而不是「失败数恰好等于独立推算值」。**没有发现钉不住的变异**：三个可能落空的地方
（M2 的耐久、M4 的耐久、M5 的槽位）我都回去核了 fixture 常量——
`HeldDurability = 25`、`Bag.OpenedSlot = 12`、入场 fixture 的 `Durability = 50`，均非零，
不存在「变异后恰好等于原值」的空转。M2 只红一条是被显式披露过的、正确的说明。

### 五、那处自我更正成立

我独立核了参照日志 `E:/Project/DNF/ServerS4A21/_reference-backup-20260826/packet_log.txt`：

- 822 行 `SEND cmd=0x01 type=0x0047 len=48 body=(33B)`，824 行
  `SEND cmd=0x00 type=0x000E len=119 body=(104B)`，两者相邻无插帧。`104 = 3 + 1×101` 成立。
- 824 行 body 前 13 字节 `00 | 01 00 | 00 00 | 00 00 00 00 | 91 00 00 00`
  = 列表类型 Main、条目数 1、槽位 0、物品 id 0、数量 145。与记录所写一致。
- 148 行 `NOTI 0x000D` 主背包快照 body 1419 = `5 + 14×101`，头三条
  `00 00 | 00 00 00 00`、`01 00 | 01 00 00 00`、`02 00 | 02 00 00 00`
  ——槽 n / id n / 数量 0，正是本仓 `WriteVirtualInventoryItem` 对槽 0..2 的写法
  （`fixedItemId = slot`）。**所以「那条样本装的是虚拟槽 0 而不是一件装备」成立。**
- 236 行入场那一帧 508 字节 5 条 `0xB1`..`0xB5`，与本页 A21 一节一致。
- 我另外全文数了一遍：整份日志里 `type=0x000E` **只有 236 与 824 两帧**，
  `type=0x0047` 只有 822 一帧。因此「参照如何用本帧描述装备到货并无样本」
  这句**全称否定在这份日志的范围内是可核的、且为真**。

**这个区分成立**，而且方向是保守的一侧：拿到了沾边的证据，却明确写下它证不到的那一半。
这正是本 Spec 要的做法。

### 六、漏网断言：只有一处，不阻塞

扫过本轮改动文件里对本文件之外的可验证断言，逐条找出处后核实：
`AGENTS.md:233` 的切片规则、`ItemContainers.cs:604-610` 的无边界抛异常、
`ProtocolModule.cs:182-186` 的地面拾取同为 Transactional、
`GroundPickupSlice.cs:474-485` 的先应答后通知、`GameProtocol.cs:99/101` 的 `0x002B`/`0x0027`、
`SelectCardCommand.cs:199-200` 的「`StorageEntryId` 在该文件里只出现在一处 avatar 报错文案」
（`GameInventoryDelivery.cs:49`，确为唯一一处）——**全部属实**。

**唯一一处站不住的**：本文件「意外与处理」第 1 条（行 304-305）写
「这条日志同时坐实了两件事：**「先应答后通知、同一次交换」是参照的行为**」。
日志逐帧记 `SEND`，能坐实的是**顺序与相邻**，坐实不了两帧是否在**同一次交换/同一次写**里
发出——这恰是 M8 要区分的那个性质。协议记录 `NOTI-0x000E.md` 本身写得是对的
（「顺序在本仓的实现……这既是上面那段参照的**顺序**」），Self-check 第 4 条的措辞
（「参照……也是这个顺序」）也是对的；只有本文件叙述这一句多说了「同一次交换」。

**不阻塞的理由**：验收条件 4 的「同一次交换」依据的是仓内地面拾取先例（我已亲自比对），
不依赖这句；落进仓库的协议记录未被污染；判据也没有因此变弱。
**建议（非阻塞）**：把行 304-305 的「先应答后通知、同一次交换」改为
「先应答后通知、中间无插帧」，与协议记录取齐。

### 七、能否收工

对照 `Contract Amendment 1` 修订后的六条：

1. 同一条投递、翻牌切片不引用 StateSynchronization —— 达成（grep 零引用 + 端点层归属判据）。
2. 入场字节逐字节不变、既有判据原样通过 —— 达成（函数体逐字符相同 + 上提前在案的 SHA +
   既有断言未改 + 全量 1873/0/2）。
3. `Transactional` —— 达成，且是功能性必需。
4. 先应答后通知、同一次交换 —— 达成，先例已比对。
5. `NOTI-0x000E.md` 人工区补记第二个调用方 —— 达成，且引用的日志证据我复核为真。
6. 全量不低于 1690 通过 / 0 失败 / 2 跳过 —— 达成（1873 / 0 / 2，我自己跑出）。

两条控制器裁定的要求（许可清单补记后 `ProtocolModule.cs` 与两个 `protocol/` 文件在界内；
变异还原须保证重编译且至少观察到一次变红）也已满足。
**TASK-06 可以收工**，第六节那一句建议按非阻塞项处理。
