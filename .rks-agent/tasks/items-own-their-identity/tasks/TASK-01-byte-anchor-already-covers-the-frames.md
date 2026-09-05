# TASK-01 — 确认逐值不变的字节锚已经存在并覆盖本次要碰的帧

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 指认既有的入场帧哈希守卫覆盖了本轮要碰的三类帧（物品清单、契约兽清单、增量），并把覆盖清单写进本文件，作为后续每一次「逐值不变」声称的当场可核之锚。**本 Task 不写生产代码。**
- Referenced spec requirements: RQ-011
- Referenced plan sections: T-001
- Owned boundary: BD-001
- Permitted files or interfaces: 只读：`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`、`Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/InventoryDeltaLiftTests.cs`。写：仅本 Task 文件。
- Dependencies: 无
- Required dependency outputs: 无
- Acceptance conditions: 覆盖清单指出物品清单帧、契约兽清单帧、增量帧各自对应哪些哈希条目或哪一处判据，每一条都能当场回到文件核对；全量判据仍为基线值。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-001 `Non-command: 指认帧哈希守卫中对应物品清单、契约兽清单与增量帧的条目`
- CK-002 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败

- Required evidence: 覆盖清单（含文件与位置）＋ 一次全量判据输出的逐项目计数。

## Execution Result — round 1

**未改动任何生产代码。** 完成时 `git status --short` 只含本任务声明的六份持久产物，无源码条目。

### 字节锚的形状

守卫是 `SelectCharacterAcceptedTranscriptTests`（`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/`）。
它把整批入场帧逐帧取 SHA-256（`Sha256` 定义在该文件 `:427-428`，`Convert.ToHexString(SHA256.HashData(frame))`），
与 `AcceptedKnightFrameSha256`（`:30` 起）整表比对（断言在 `:220-222`）。
帧标识清单 `AcceptedStaticIdentities` 在 `:110` 起。

**实测：标识 62 条、哈希 62 条，一一对应。** 所以这是一张逐帧的字节表，不是抽样。

### 覆盖清单（本轮要碰的帧）

| 帧 | 是什么 | 覆盖它的哈希序号 |
|---|---|---|
| `0x0069` | 契约兽清单 | 第 **13** 条 |
| `0x000D` | 物品清单（容器快照） | 第 **18、19、20、21、22、23** 条，共六帧 |
| `0x0002` | USERINFO（subtype 0 带契约兽三元组、subtype 1 带装备条目） | 第 **2、14、57** 条 |

**`0x000E`（增量）不在这张静态清单里**——实测该类型在 `AcceptedStaticIdentities` 中出现 0 次。
它由另一处判据钉住：`InventoryDelta_UsesTheSnapshotItemEncodingAndMatchesAcceptedFrameBytes`
（同文件 `:245` 起），断言增量帧的哈希为 `F9BE81C62166A8D13FDD70545AAC31351973CFCF6A84A0421BBFAF9E0E8A27FC`
（`:274-276`），并同时断言增量与快照对同一件物品的编码一致。

**另有一处单帧哈希**：`0x01C0` 的 Job=9 变体，`53E1FD5ECF783E20C92F1B2B0549010A934265656D4A6A3AB8D24DFC957D0C65`（`:240-242`）。
与本轮无关，记下以免后来者误以为清单只有一处。

### 检查结果

| 检查 | 结果 |
|---|---|
| CK-001 | 通过。三类帧各自的覆盖位置见上表，每条可回到文件当场核 |
| CK-002 | 通过。`项目数=7 通过=2131 跳过=2 失败=0`，失败块 0 |

守卫自身单独复跑（`--filter` 限定该类与 `InventoryDeltaLift`）：**19 通过 / 0 失败**。

## Executor Self-check — round 1

- **本任务不写生产代码这一条守住了**：完成时工作区无源码改动，可由 `git status --short` 当场核。
- **数字是实测的，不是抄的**：62/62 的一一对应由脚本解析两个数组后比对得出；
  六帧 `0x000D` 的位置是枚举出来的，不是数出来的。
- **`0x000E` 的结论是「不在静态清单里」而不是「没有覆盖」**——它由另一处哈希判据覆盖，
  位置已给出。这个区分要紧：若后来者只看静态清单，会误判增量路径无保护。
- **一处我没有验证的**：六帧 `0x000D` 分别对应哪一个容器，我没有逐帧解出来。
  入场装配的容器顺序是 Main → Avatar → PersonalCargo → Pet → AccountCargo 共五个，
  而这里是六帧——**多出的一帧我没有查清**。这不影响本任务的结论（覆盖存在且逐帧），
  但后续任务若要按容器指认某一帧，必须先解决这个不一致，不能想当然按顺序对号入座。
- 本任务未建 Git 检查点、未提交。

## Independent Quick Review — round 1 (IQR-TASK-01-R1)

- Verdict: **changes_required**
- 评审的合同版本：Contract revision 1，无修订案
- Owned diff 身份：**空**。`git status --short` 只有未跟踪项 `.rks-agent/tasks/items-own-their-identity/`、`Docs/CharacterDomainCut.md`、`Docs/CharacterEntryFacesInventory.md`、`Docs/CreatureDomainDesign.md`、`Docs/ItemWorldModel.md`；`git diff` 与 `git diff --cached` 均为空。**无生产代码改动，此项属实**（那四份 Docs 的 mtime 为 09-04 23:48 至 09-05 08:55，早于本 Task 文件的 09:42，属上游阶段产物，不计入本 Task 的 owned diff）。
- 查看的证据：`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`、`Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs`、`Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs`、`Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs`，以及一次独立复跑的 CK-002。

### 独立复核的数字（均自行数过，未采信本文件）

| 声称 | 复核 | 结论 |
|---|---|---|
| 哈希 62 条 | `AcceptedKnightFrameSha256` 声明在 `:30`，条目 `:32-93`，`];` 在 `:94`，实数 **62** | 属实 |
| 标识 62 条 | `AcceptedStaticIdentities` 声明在 `:110`，元组 `:111-127`，`];` 在 `:128`，实数 **62** | 属实 |
| 一一对应 | `:222` `Assert.Equal(AcceptedKnightFrameSha256, hashes)`，`hashes` 来自 `:220` 的 `CaptureCurrentTranscript(result).Select(Sha256)`。xUnit 的数组相等同时管住长度与顺序，故是**逐帧整表**比对，不是抽样 | 属实 |
| `0x0069` 在第 13 条 | 是 | 属实 |
| `0x000D` 恰在第 18–23 条共六帧 | 是，且相邻两条为第 17 条 `0x0005`（`DungeonPermissionStateMessage`，`StateSynchronizationMessages.cs:236`）与第 24 条 `0x0465`（`EpicBookBaselineMessage`，`:191`），六帧连续无夹杂 | 属实 |
| `0x0002` 在第 2、14、57 条 | 是 | 属实 |
| `0x000E` 不在静态清单 | 是，`AcceptedStaticIdentities` 中出现 0 次 | 属实 |
| 增量由 `InventoryDelta_UsesTheSnapshotItemEncodingAndMatchesAcceptedFrameBytes` 覆盖 | 该用例在 `:245`，哈希断言 `Assert.Equal("F9BE81C6…", Sha256(deltaFrame))` 在 **`:273-275`**（本文件写 `:274-276`，差一行，指向正确），另有 `:276-278` 的快照/增量同编码断言 | 属实，行号微偏 |
| `0x01C0` Job=9 单帧哈希 | 断言在 **`:239-241`**（本文件写 `:240-242`，同样差一行） | 属实，行号微偏 |
| `Sha256` 定义 | `:427-428`，`Convert.ToHexString(SHA256.HashData(frame))` | 属实 |

### CK-002 独立复跑

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`，退出码 0。逐项目相加（未用 `grep -c`）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 517 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 256 | 0 | 0 |
| Dfo.Server.Application.Tests | 1017 | 0 | 0 |
| **合计（7 个项目）** | **2131** | **2** | **0** |

跳过的两条是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。**与基线 2131/2/0 完全一致。CK-002 成立。** 无失败，故不涉及偶发失败的判别。

### 发现（需要整改的一条）

**「多出的一帧」已查清，而且它推翻了覆盖清单里的一行标注。**

- `SelectedCharacterStateMessageFactory.cs:142-168` 的 `AddInventorySnapshots` 只发 **五**帧，顺序写死为 `Main → Avatar → PersonalCargo → Pet → AccountCargo`（`:147-153`）。
- 第六帧来自紧随其后的 `SelectedCharacterStateMessageFactory.cs:72` `messages.Add(new GuildMedalBaselineMessage())`。
- `GuildMedalBaselineMessage` 定义在 `StateSynchronizationMessages.cs:221-225`：`Type => 0x000D`，`EncodeBody() => [(byte)InventoryContainerType.GuildMedal, 0, 0]`，即恒定三字节 `26 00 00`（38 号容器 + 零计数）。`InventoryContainerType.GuildMedal = 38` 见 `InventoryModels.cs:22`。

因此静态清单第 18–23 条的真实构成是：

| 序号 | 来源 |
|---|---|
| 18 | `InventorySnapshotMessage`(Main = 0) |
| 19 | `InventorySnapshotMessage`(Avatar = 1) |
| 20 | `InventorySnapshotMessage`(PersonalCargo = 2) |
| 21 | `InventorySnapshotMessage`(Pet = 7) |
| 22 | `InventorySnapshotMessage`(AccountCargo = 12) |
| 23 | `GuildMedalBaselineMessage`（**不是容器快照**，是一条恒定空基线，不走物品编码路径） |

（`StateSynchronizationMessages.cs:209-220` 的注释与 `SelectCharacterAcceptedTranscriptTests.cs:100` 的「a sixth 0x000D, the guild-medal container A21 added」都这么写；但结论是由上面三处**代码**定的，注释只作旁证。）

**「不影响本任务结论」这个说法只对了一半。** 对的一半：覆盖确实存在且是逐帧整表，六帧全部被字节钉住，这一点不受影响。错的一半：交付物本身失真了——覆盖清单的 `0x000D` 行把六帧一律标成「物品清单（容器快照）」，而第 23 条根本不是容器快照，它的字节与 `items` 表、物品编码、槽表全都无关，**无论后续怎么改物品建模它都不会动**。这条清单的用途正是给后续「逐值不变」声称当锚，一个把恒定帧误当敏感帧的锚会两头误导：既可能让人以为「六帧都没变」等于「五个容器都没变」（漏掉某个容器的快照缺失也照样绿），也可能让后来者按位置把容器对号入座时整体错位。执行方自己已经记了「不能想当然按顺序对号入座」，但清单正文没改，读者只看表格就会被带偏。

### 要求的整改（局部、低成本）

1. 把覆盖清单里 `0x000D` 那一行拆开：第 18–22 条 = 五个容器快照（按上表标出容器），第 23 条 = `GuildMedalBaselineMessage` 恒定空基线，并注明它不参与物品编码。
2. 相应地把自检里「多出的一帧我没有查清」改成已查清，并给出上面三处代码位置（`SelectedCharacterStateMessageFactory.cs:72` / `:147-153`、`StateSynchronizationMessages.cs:221-225`）。
3. 顺手把两处差一行的行号订正为 `:273-275`（增量哈希）与 `:239-241`（`0x01C0` 哈希）。

其余各项——不写生产代码、62/62 一一对应、三类帧的定位、`0x000E` 的替代覆盖、CK-002 计数、未建检查点未提交——均独立核实成立，无其它阻断缺陷。

## Execution Result — round 2

针对 IQR-TASK-01-R1 的整改。**只改本 Task 文件，未改动任何生产代码**
（`git status --short -- Server/` 为空）。round 1 的记录原样保留，不回改。

### 订正一：第 23 帧不是容器快照，覆盖清单据此拆开

快审查清了那第六帧，我独立核实为真：

- `GuildMedalBaselineMessage` 定义在
  `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs:221-225`：
  `Type => 0x000D`，`EncodeBody() => [(byte)InventoryContainerType.GuildMedal, 0, 0]`——
  **恒定三字节**（`GuildMedal = 38`，见 `Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs:22`）。
- 它由 `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs:72`
  发出，**不在** `AddInventorySnapshots` 里。
- 容器快照清单恰好五项（同文件 `:147-153`）：Main、Avatar、PersonalCargo、Pet、AccountCargo。

**订正后的覆盖清单：**

| 帧 | 是什么 | 哈希序号 |
|---|---|---|
| `0x0069` | 契约兽清单 | 第 **13** 条 |
| `0x000D` | **容器快照，五帧**：Main / Avatar / PersonalCargo / Pet / AccountCargo，按此顺序 | 第 **18–22** 条 |
| `0x000D` | **公会勋章空基线，不是容器快照**——恒定三字节 `26 00 00`，不走物品编码路径 | 第 **23** 条 |
| `0x0002` | USERINFO（subtype 0 带契约兽三元组、subtype 1 带装备条目） | 第 **2、14、57** 条 |

`0x000E`（增量）仍不在静态清单里，替代覆盖不变（见 round 1）。

### 订正二：两处行号差一行

哈希串本身在 `:240`（`0x01C0` 单帧）与 `:274`（增量帧）；`Assert.Equal(` 在各自上一行。
所以断言跨度是 **`:239-241`** 与 **`:273-275`**，round 1 写成 `:240-242` 与 `:274-276` 是错的。

### 检查结果（重跑）

| 检查 | 结果 |
|---|---|
| CK-001 | 通过。订正后的清单见上表，每条可回到文件当场核 |
| CK-002 | 通过。`项目数=7 通过=2131 跳过=2 失败=0`，失败块 0 |

## Executor Self-check — round 2

- **快审推翻的是我的判断，不是我的数字。** round 1 的 62/62、13、18–23、2/14/57、`0x000E` 为零，
  逐条经独立复核属实。错的是我给那处未查清盖的章——**「不影响本任务结论」我没有证据就写了下去**，
  而这个 Task 的交付物**就是**那份清单，清单失真即交付物失真。
- **失真的具体后果**（这是这条整改真正要挡住的）：清单的用途是给后续「逐值不变」当锚。
  把一条恒定帧标成容器快照会两头误导——让人以为「六帧没变」等于「五个容器都没变」，
  或者按位置对号入座时整体错位一帧。
- **两处行号错的原因值得记下**：我引用的是**哈希串所在行**，写出来的却是**断言跨度**，
  两者差一行。以后引用跨度就核跨度的首行，不要拿串的行号去推。
- **本轮仍未改动任何生产代码**，可由 `git status --short -- Server/` 为空当场核。
- 本任务未建 Git 检查点、未提交。

## Independent Quick Review — round 2 (IQR-TASK-01-R2)

- Verdict: **passed**
- 评审的合同版本：Contract revision 1，无修订案
- 审查范围：**round 2（整改轮）**。round 1 与 IQR-TASK-01-R1 只作对照，未回改。
- Owned diff 身份：**空**。`git status --short -- Server/` 无输出；`git diff --stat` 与 `git diff --cached --stat` 均为空；`git status --short` 只有四份上游 `Docs/*.md` 与 `.rks-agent/tasks/items-own-their-identity/` 未跟踪项。**「本轮未改动任何生产代码」属实。**
- 查看的证据（均自行打开核对，未采信本文件中的任何数字或行号）：
  `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`、
  `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs`、
  `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs`、
  `Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs`，以及一次独立复跑的 CK-002。

### 1. 整改是否对上了被打回的那一条

对上了。round 2 的覆盖清单把 `0x000D` 拆成**两行**：第 18–22 条标为「容器快照，五帧」，第 23 条单列为「公会勋章空基线，不是容器快照——恒定三字节 `26 00 00`，不走物品编码路径」。IQR-TASK-01-R1 要求的三项整改逐条落地：拆行（订正一）、自检里「多出的一帧未查清」改为已查清并给出三处代码位置（Execution Result round 2 与 Executor Self-check round 2）、两处行号订正（订正二）。**交付物失真这一条已消除。**

### 2. 第 23 帧的三处代码位置（逐条独立核对）

| 声称 | 我核到的 | 结论 |
|---|---|---|
| `StateSynchronizationMessages.cs:221-225` 的 `GuildMedalBaselineMessage` | `:221` `internal sealed record GuildMedalBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage`，`:222` `{`，`:223` `internal override ushort Type => 0x000D;`，`:224` `internal override byte[] EncodeBody() => [(byte)InventoryContainerType.GuildMedal, 0, 0];`，`:225` `}` | 属实，跨度精确 |
| `Type => 0x000D`、`EncodeBody()` 恒定三字节 | 见 `:223`/`:224`；无入参、无状态，恒定 | 属实 |
| `InventoryModels.cs:22` 的 `GuildMedal = 38` | `:22` `GuildMedal = 38,` | 属实；38 = `0x26`，故 `26 00 00` 的字节写法正确 |
| `SelectedCharacterStateMessageFactory.cs:72` 的发出点 | `:71` `AddInventorySnapshots(messages, result);`，`:72` `messages.Add(new GuildMedalBaselineMessage());` | 属实，且确实**不在** `AddInventorySnapshots` 里 |
| `:147-153` 容器清单恰好五项 | `:142` 方法签名，`:147` `[`，`:148-152` 五个元素 Main / Avatar / PersonalCargo / Pet / AccountCargo，`:153` `];` | 属实，恰好五项 |

### 3. 第 18–22 条的容器顺序：**经代码确证，不是未经证实的声称**

我没有因为工厂里的数组是这个顺序就默认帧序也是。确证链条如下，每一环都可当场回文件核：

- `SelectCharacterAcceptedTranscriptTests.cs:400-402` 的 `CaptureCurrentTranscript` = `EncodeThroughRegisteredEncoders(SelectedCharacterStateMessageFactory.Create(result))`——直接吃工厂返回的**有序** `messages` 列表。
- `:414-422` 的编码循环 `foreach (var message in messages) { …; frames.AddRange(encoder.Encode(message)); }`——按消息顺序追加，**保序**，无排序、无分组。
- `:220-222` 的断言 `Assert.Equal(AcceptedKnightFrameSha256, hashes)` 走 xUnit 数组相等，同时管住长度与顺序。
- 工厂发出顺序（`SelectedCharacterStateMessageFactory.cs:70-73`）：`DungeonPermissionStateMessage`（`0x0005`）→ `AddInventorySnapshots`（五帧 `0x000D`）→ `GuildMedalBaselineMessage`（`0x000D`）→ `EpicBookBaselineMessage`（`0x0465`）。
- 静态清单 `AcceptedStaticIdentities`（`:111-127`）对应位置：第 **17** 条 `(0, 0x0005)`、第 **18–23** 条六个 `(0, 0x000D)`、第 **24** 条 `(0, 0x0465)`——**前后邻居与工厂发出顺序严丝合缝**。
- **1:1 的额外佐证**：这一段是 6 条消息（5 快照 + 1 勋章基线）对 6 个 `0x000D` 帧。若任一快照被编码器切成多帧，帧数会 > 6。实测恰好 6，故此段消息与帧一一对应，不存在错位。

因此「第 18–22 条 = Main / Avatar / PersonalCargo / Pet / AccountCargo，第 23 条 = 勋章基线」是**代码可推的结论**，不是猜测。

**一处非阻断的证据薄弱点**（不要求整改）：round 2 正文只举了工厂里的数组顺序，没有写出「捕获保序」这一环。结论为真，但读者若只按正文给的位置核，链条差一环。上面 `:400-402` / `:414-422` / `:220-222` 三处补齐了它，后来者可直接引用。

### 4. 两处行号订正

| 订正 | 我核到的 | 结论 |
|---|---|---|
| `0x01C0` 单帧断言 `:239-241` | `:239` `Assert.Equal(`，`:240` `"53E1FD5E…"`，`:241` `Sha256(frame));` | **订正正确** |
| 增量帧断言 `:273-275` | `:273` `Assert.Equal(`，`:274` `"F9BE81C6…"`，`:275` `Sha256(deltaFrame));` | **订正正确** |
| 「哈希串本身在 `:240` 与 `:274`」 | 同上 | 属实 |

### 5. round 1 未被打回的数字，本轮重新独立核过，仍成立

| 声称 | 复核 | 结论 |
|---|---|---|
| 62 条标识 ↔ 62 条哈希一一对应 | `AcceptedKnightFrameSha256` 声明 `:30`，条目 `:32-93` 共 **62**；`AcceptedStaticIdentities` 声明 `:110`，元组 `:111-127` 共 15 行×4 + 1 行×2 = **62**；`:220-222` 整表按序比对 | 属实 |
| `0x0069` 在第 13 条 | 第 13 条 `(0, 0x0069)` | 属实 |
| `0x0002` 在第 2 / 14 / 57 条 | 三处均为 `(0, 0x0002)`，清单中 `0x0002` 恰好出现 3 次；`StateSynchronizationMessages.cs` 中 `Type => 0x0002` 也恰有三处（`:73`、`:85`、`:497`），三条帧来自三个不同消息类型 | 属实 |
| `0x000E` 在静态清单中出现 0 次 | 逐条扫过 62 个元组，无 `0x000E` | 属实 |
| 增量由 `InventoryDelta_UsesTheSnapshotItemEncodingAndMatchesAcceptedFrameBytes` 覆盖 | 用例在 `:245`，哈希断言 `:273-275`，快照/增量同编码断言 `:278-280` | 属实 |

### 6. CK-002 独立复跑

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`，退出码 **0**。按项目逐个相加（未用 `grep -c`）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Dfo.Server.Protocol.Tests | 517 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 256 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Application.Tests | 1017 | 0 | 0 |
| **合计（7 个项目）** | **2131** | **2** | **0** |

跳过的两条仍是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。**与基线 2131 / 2 / 0 完全一致，CK-002 成立。** 零失败，不涉及偶发失败判别。

### 结论与一条非阻断记录

round 2 的两项订正**逐条经独立核实为真**，被打回的那一条已消除，round 1 未被打回的数字重核后仍成立，owned diff 仍为空，全量判据与基线一致。CK-001、CK-002 均通过，本轮无阻断缺陷。

- **非阻断记录（不要求本 Task 整改）**：契约 Required evidence 写的是「一次全量判据输出的**逐项目**计数」，而 round 1 与 round 2 的结果表给的都是聚合式 `项目数=7 通过=2131 跳过=2 失败=0`，逐项目明细只出现在两轮快审里。数字本身已由本轮独立逐项目复跑证实无误，故不构成阻断；后续 Task 若沿用这个格式，直接把逐项目表写进 Execution Result 更省一次复核。
