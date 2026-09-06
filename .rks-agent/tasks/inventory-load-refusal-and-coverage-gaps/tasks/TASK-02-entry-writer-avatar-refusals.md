# TASK-02 — 钉住条目写出侧的两条时装拒绝

## Task Unit Contract

- Revision: 1
- Outcome and references: 覆盖 RQ-005/RQ-006 / AC-006..AC-008 / plan T-002。**不改生产代码**：为 `GameInventoryItemEntry.Write` 已有的两条拒绝各补一条判据。
- Owned boundary:
  - 判据：`Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/` 目录（并入 `InventoryDeltaLiftTests.cs` 或新建同族文件）。
  - **生产代码一字不动**（变异核实期间的临时改动除外，且必须还原并核散列）。
  - 不得改动 `SelectCharacterAcceptedTranscriptTests.cs`。
- Required checks:
  1. 两条新判据直接绿（被钉的行为是旧的）。
  2. 全量判据退出码 0，跳过数仍为 2。
  3. CK-002（＝AC-008）：分两次，分别删去 `Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs` 第 51 行与第 113 行的 `throw`，确认对应判据各自变红；每次还原并 `sha256sum -c` 通过。
- Dependencies: 无

## Result (round 1)

- **Changed:** `Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/EntryWriterAvatarShapeTests.cs`（新建，4 条判据：1 条非时装带细节 ＋ 1 条 `[Theory]` 两例珠宝槽 29/31 字节 ＋ 1 条正例）。**生产代码零改动**。
- **Why:**
  - 未并入 `InventoryDeltaLiftTests.cs`：那个文件的主题是「搬迁前后逐字节相同」，混入拒绝判据会稀释它。同目录另立一个文件。
  - 珠宝槽用 `[Theory]` 取 29 与 31 两侧，而不是只取 29：`!= 30` 的两侧都要落到，只测一侧的话把条件写成 `< 30` 也能通过。
  - 加了正例 `AnAvatarWithItsThirtyBytesIsWrittenAsTheLongerEntry`。两条拒绝判据单独存在时，一个「对所有时装都抛」的实现也能让它们全绿；正例把这个读法排除掉。它同时是全仓**唯一**一条对时装条目长度的断言——既有的帧散列锚覆盖的都是 kind 2 的条目。
  - 42 字节尾部按 AGENTS.md「一个由多项相加得出的数要么当场写出加法」写成注释里的 `4 + 30 + 4 + 2 + 2 = 42`；该加法已被真实编码器验证（正例断言通过）。
- **Checks:**
  - 新判据 4/4 通过。
  - 全量判据：退出码 **0**，**2247 总 / 2245 通过 / 2 跳过 / 0 失败**（2237 ＋ 4 ＋ 4）。落盘：`scratchpad/task02-full.txt`。
  - CK-002 变异核实，散列 `18c22acf2209378aa5e947899d8559f7385dc976aae895dac1e99f67f6ff2dd5`：
    - 变异 A（删去第 51 行 `if` ＋ `throw`）→ `AnItemThatIsNotAnAvatarMayNotCarryAvatarFacts` 变红，另 3 条仍绿。还原 `sha256sum -c` ＝ OK。
    - 变异 B 首次尝试按「删去 `if` ＋ `throw`」做，**撞上 CS8602**：`avatar!` 的空值宽恕就写在被删的那一行上，删掉即编译不过（`TreatWarningsAsErrors`）。改用等效的**条件失效**变异 `if (avatar!.JewelSocket.Length < 0)`——保留宽恕、让守卫永不成立。结果：`AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused` 的 29 与 31 两例双双变红，另 2 条仍绿。还原 `sha256sum -c` ＝ OK。
- **Remaining:** 无。记一条方法上的教训供后续单元用：**当守卫所在行同时承载空值宽恕时，「删掉守卫」不可编译，等效变异是把条件改成恒不成立。**

## Independent Quick Review (IQR-02-1)

- Reviewed execution round: Result (round 1)
- Contract revision: 1（无修正案）
- Owned diff identity: 新增未跟踪文件 `Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/EntryWriterAvatarShapeTests.cs` 一个；`git diff -- Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs` 为空，且该文件 sha256 ＝ `18c22acf…6ff2dd5`，与变异前记录一致。`git status --porcelain` 里其余两处属于别的单元；`SelectCharacterAcceptedTranscriptTests.cs` 零差异；全工作树无 `.bak`/`.orig`/`.rej`。

### Verdict: passed

### 复核到的事实

- **两条拒绝确实被钉住，不是碰巧变绿。** 非时装那条（`GameInventoryDelivery.cs:50-51`）：入参 kind ＝ 2 且 `AvatarDetail` 非空，守卫若不在，写出走 `core.Value` 分支、跳过 `:110` 的时装尾部，正常返回 104 字节而不抛——变异 A 的结果与此一致。珠宝槽那条（`:112-113`）：`[Theory]` 取 29 与 31 两侧，把 `< 30` / `> 30` 两种偏写都挡在外面；到达该守卫前的每一步（`checked((short)3)`、`Math.Max(0, ExpireDate − preparedAt)`、两处 `PopulatedCount` 的 `checked((byte)…)`）在本夹具取值下都不会抛，因此绿不可能来自别处。两条都用 `Assert.Equal` 比全文消息，别的 `InvalidDataException` 落不进来。
- **`Assert.Equal` 全文消息在这里是必要的，不是脆。** 同仓另有两条近乎同名的拒绝（`StateSynchronizationPayloadCodec.Entry.cs:187` 的 `Support avatar jewel socket must contain exactly 30 bytes.` 与 `:770` 的 `Mailbox avatar …`）。用 `Contains` 会把这三条混为一谈；而 AC-006/AC-007 本身就把措辞写进了验收，全文相等正是它要的强度。
- **3 ＋ 101 ＋ 42 ＝ 146 逐字节独立复核通过。**
  - 3：`EncodeBody` 写 `(byte)ContainerType` ＋ `checked((ushort)Count)` ＝ 1 ＋ 2（`GameInventoryDelivery.cs:178-179`）。
  - 42：时装尾部 `:114-118` 依次是 `Write(30)` 的 int 4、`JewelSocket` 30、`Write(4)` 的 int 4、`Color1`/`Color2` 各 2（`InventoryModels.cs:155-156` 为 `ushort`）——注释里的 `4 + 30 + 4 + 2 + 2 = 42` 各项、项数、和三样齐且正确。
  - 101：`:53-107` 逐行按 `InventoryItemCoreState`（`InventoryModels.cs:81-115`）的字段宽度累加得 84，加 `:108` 的 `CommonLength − WrittenLength` ＝ 17 零字节，合 101。正例通过本身也反证了这两个数。
- **变异核实的偏离已记且等效。** 契约要求「删去第 113 行的 `throw`」，执行改为把条件写成恒不成立。原因成立：`avatar!` 的空值宽恕就在 `:112`，删该行后 `:115` 的 `avatar.JewelSocket` 触发 CS8602，而 `Server/Directory.Build.props:6` 开着 `TreatWarningsAsErrors`。条件失效在证据强度上等价（保留守卫所在行、只让它永不成立），两例双双变红。
- **落盘证据核对通过。** 全量日志 `scratchpad/task02-full.txt`：测试运行成功、跳过数 2、四条新用例逐条「已通过」。（该文件记的 2245 通过与后续单元跑出的读数不同，是 TASK-03 等又加了用例，不构成矛盾。）
- **注释量在基线附近。** 新文件非空 81 行、注释 28、代码 53，比 **0.53**；同目录 `InventoryDeltaLiftTests.cs` 为 75/126 ＝ **0.60**。不是 6.20 那一类。
- **无分层与构建风险。** 新文件只用 `Dfo.Server.Data.Inventory` 与 `Dfo.Server.Protocol.Game`，与同目录既有文件一致；无主构造器，不涉 CS9113；全量构建在 `TreatWarningsAsErrors` 下通过。

### 观察（不构成缺陷，不要求改）

- 与 `InventoryDeltaLiftTests.cs` 重复的有 `ObservedEntryLength`/`ObservedDeltaHeaderLength`、`PreparedAt` 与 `Core(…)` 工厂。前两个是**有意的第二次转录**——两个文件都自陈「Transcribed rather than read off the constant」，若改成引用 `GameInventoryItemEntry.CommonLength`，正例的长度断言就会自证自话，重复在这里是判据独立性的代价而非漂移点。`Core(…)` 的重复纯属样板，但字段一变两处都编译不过，漂不掉；plan「Choices During Implementation」也把「新建文件还是并入」留给了执行。判为可接受。
- 本单元边界之外，供后续参考：`StateSynchronizationPayloadCodec.Entry.cs:187` 与 `:770` 两条同族珠宝槽拒绝今天仍无判据。RQ-005/RQ-006 只点名 `GameInventoryItemEntry.Write`，不影响本单元验收。
