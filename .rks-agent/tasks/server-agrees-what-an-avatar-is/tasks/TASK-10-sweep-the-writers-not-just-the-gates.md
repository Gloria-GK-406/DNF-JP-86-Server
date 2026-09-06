# TASK-10 — 扫写入方，不只扫判定点

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

把完整变更评审指出的方法缺口补上：**反向扫此前只枚举了判定点（拒绝与放行），
没有枚举写入方**，而第 37 条缺陷的制造者正是一个写入方。
并订正一处可一次减法证伪的算术错。

- Referenced spec requirements: RQ-008（清单完整性）、RQ-009（仓库约定如实）
- Referenced plan sections: 本任务由 `reviews/change-review.md` 的阻断发现与 NB-1 新增
- Owned boundary:

BD-006 `.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md`
BD-007 `AGENTS.md`
BD-011 承载「剩余秒数」说法的生产注释（**仅限该处措辞订正，不得改任何可执行代码**）

- Dependencies: TASK-09
- Required dependency outputs: TASK-09 的修复与它列出的 `PutAsync` 生产调用方清单
- Acceptance conditions:

1. **清单新增一节「制造者扫」**：枚举本轮五处起因所能产生的**写入方**
   （不是判定点），对每个回答：它能否产生一行「kind 8 但六列为空」的时装？
   有没有判据覆盖？**这一节要能独立于「判定点扫」被读懂。**
2. **第 37 条（地面拾取）记进清单**，含：制造者是谁、后果是什么、
   由谁发现（**完整变更评审，不是执行方自查**）、TASK-09 怎么修的、修后有无覆盖。
3. **订正 NB-1 的算术**：清单与生产注释里「约 2.1×10⁹／two billion」是错的。
   `int.MaxValue − now ≈ 3.6×10⁸` 秒，**约 11.4 年**。
   按仓规**补记修正案并写明原文**，不是默默改数。
   **这一条影响所有者实机时判断客户端渲染是否正常，要写清楚。**
4. **把「未测定／无覆盖」按后果分档**，让读者知道**先看哪个**，
   而不只是知道**哪些没量**。分档标准要写明。
5. `AGENTS.md` 相应段落同步更新，含第 37 条与订正后的秒数。

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。**禁止任何 git 还原类命令**。
**不得改动任何可执行代码或判据**（BD-011 仅限注释措辞）。

- Blocking conditions:

若「制造者扫」查出**新的、TASK-09 未覆盖的**同型缺陷，
**停下并提交阻塞报告**，不要自行扩范围去修。

- Required methods:

- 记法：**不得出现读者无法证伪的全称量词**。只写①观测（附出处）②明写「不足以确定…」。
  **但不得因此把该说的话压掉**——评审指出记载要能被读者用来做决定。
- **引用他人原话时出处必须是持久记载并逐字相符。**

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-030 | Non-command：逐句核对新写下的记载 | 每一句都能指回一处实测或一处代码；没有一句比证据说得更强，也没有把该说的话压成「不足以确定」 |
| CK-031 | Non-command：复算「剩余秒数」 | 给出 `int.MaxValue − now` 的实际算式与结果，读者可自行复算 |
| CK-032 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败，EXIT=0；本任务不改可执行代码，计数应与 TASK-09 结束时相同 |

- Required evidence:

- 「制造者扫」那一节的全文与它的枚举口径
- 第 37 条的记法前后对照
- NB-1 订正的前后对照与复算过程
- 分档标准与分档结果
- 三条检查的实际输出

- Constraints:

- **不得改动任何可执行代码或判据**
- 不写读者无法证伪的基数
- 按成员名索引，不按行号

---

## 执行结果

本节是**追加**。上面的 `## Task Unit Contract` 一节一字未删未改。

**本任务没有改动任何可执行代码或判据。** 生产侧唯一动过的是
`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` 里
`ArrivingAvatar.NeverExpires` 的 `<remarks>`（BD-011，只改注释措辞）。
**未提交、未建检查点、未使用任何 git 还原类命令，未触碰主检出与 `.worktrees/client-debug-lab`。**

### 〇、改了哪三个文件

| 边界 | 文件 | 改了什么 |
|---|---|---|
| BD-006 | `.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md` | 新增 3.7「制造者扫」、3.8「按后果分档」、3.6 的 RX-03、3.1 表后的「RV-08 的两个制造者」、第五节的 NB-1b；改写文件头（第三版说明）与第〇节散列表的一处例外说明；RP-06 那一栏就地标注秒数订正；三处计数同步（第三节合计 36→37、3.5 里 3.6 那一行、3.6 标题 2→3） |
| BD-007 | `AGENTS.md` | 「做成了三件事」→「做成了四件事」并新增第 4 件（地面拾取那一条，含制造者／后果／发现者／修法／覆盖／一处不足以确定，以及它暴露的方法缺口与 3.7 节的指路）；「实机须知，两条」→「三条」并新增第 3 条（剩余秒数约 11.4 年）；「补记的四条修正案」→「五条」并新增其五（秒数订正，原文两处逐字抄下＋算式） |
| BD-011 | `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | `ArrivingAvatar.NeverExpires` 的 `<remarks>`：把 "remaining seconds are two billion" 改成 "remaining seconds are what this bound leaves"，并另起两段 `<para>`——一段把原句逐字引出来、写明它错在哪、附算式；一段写清这个数对所有者实机判断的影响 |

**该文件本次改动是不是只有注释，可当场核**：改动落在第 69–104 行这一段；
把第 68–105 行里非 `///` 开头的行取出来，只有两行——一行空行与
`private const int NeverExpires = int.MaxValue;`，两行都未被本次改动触及。
（命令：`awk 'NR>=68 && NR<=105' <文件> | grep -nvE '^\s*///'`，读数如上。）

**散列**：`ItemContainers.cs` 本次改动后
`be6380973bdcc9a1cd8b4fd7210f992cf7ee504ae53968e546f4bcd66955c4d6`
（改动前是清单第〇节记的第二版交付态 `8e7f085d…`）。
**字节锚** `SelectCharacterAcceptedTranscriptTests.cs`：
`1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`，与契约给的相符，**未动**。
`GroundPickupSlice.cs`：`cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d`，
与 TASK-09 交付态相同，**未动**。
`git status --porcelain`：**21 行 ＝ 14 个 ` M` ＋ 7 个 `??`**，与 TASK-09 收尾读数逐行相同
（本任务改的三个文件里，两个本来就在那 14 个 ` M` 里，第三个落在已是 `??` 的
`.rks-agent/tasks/server-agrees-what-an-avatar-is/` 目录内，行数与分类都没变）。

---

### 一、「制造者扫」那一节的全文与它的枚举口径

**全文落在清单的 3.7 节**（`### 3.7 制造者扫：谁能写出一行「kind 8 但六列为空」的时装（第三版新增）`），
含：开头的问题界定与三步机理、**枚举口径三条**、3.7.1（容器一族 11 行）、
3.7.2（入场存盘一族 2 行）、3.7.3（小结与复算）、末尾「这个枚举口径挡不住什么」六条。
下面只把**枚举口径**与**结论**抄在这里，全文见清单。

**这一节独立于「判定点扫」被读懂**，做法是：它开头就写明 3.1–3.6 问的是「判定点」、
本节问的是「写入方」，并且把要盯的形状（「`item_core` 第 0 字节是 8，而六列是 NULL」）
与它的后果（三步：`FillAsync` 算不出六列 → `WithAvatarDetail` 抛 → `SelectCharacterHandler`
的 `catch (Exception error)` 关连接）从头讲了一遍，不要求读者先读前面几节。

**枚举口径（清单原文的三条）**

- **口径一**：「写入方」＝ 一个能把一行物品核心交到世界表 `items` 上的生产成员。
  在 `Server/src` 下按写入函数名检索，今天读到**两族**：容器一族经
  `StoreBackedItemContainer.WriteOneAsync`；入场存盘一族经 `ICharacterItemTable` 的
  `InsertOwnAsync`／`UpdateOwnAsync`（生产调用方落在 `InventoryEntryAssembly` 与
  `CharacterRosterService` 两个文件里）。**本任务亲自跑的只读检索。**
- **口径二**：容器一族的汇流口只有 `WriteOneAsync` 一个。
  `grep -rn "WriteOneAsync" Server/src --include=*.cs` 读出 **3 行**：声明本身
  与两个调用点——`PutAsync`（**恒传 `null` 事实**）与 `LandAsync` 的 `IntoEmptySlot` 分支
  （可传 `ArrivingAvatar.Facts()`）。**本任务亲自跑的只读检索。**
- **口径三**：`PutAsync` 的生产调用方取 TASK-09 第三节：**15 个调用点、落在 10 个成员上**
  （`grep -rn "PutAsync" …` 21 行 − 6 行非调用点 ＝ 15 行；15 个调用点按宿主成员去重得 10 个）。
  **出处 TASK-09 第三节与它第二轮的「订正一」，本任务未重跑该 grep。**
  `Server/tests/` 下的测试替身不算生产写入方。

**行数的复算**：容器一族 10 个成员，其中 `LandAsync` 两支各占一行 → **11 行**；
入场存盘一族 2 个成员各一行 → **2 行**。**11 ＋ 2 ＝ 13 行**，与 3.7.1／3.7.2 两张表的行数相同
（本任务用脚本数过两张表的行：11 与 2）。

**13 个写入方，逐个列名**（第三栏「同型」＝ 改不改站着那行的 `Core.Value`）：

| # | 写入方（成员名） | 同型 | 今天能被时装走到 | 判据有无 |
|---|---|---|---|---|
| 1 | `StoreBackedItemContainer.LandAsync` 的 `IntoEmptySlot` 分支 | 不适用 | 能 | 有覆盖 |
| 2 | `LandAsync` 的 `OntoExistingPile` 分支 | 同型 | 走不到 | 未测定 |
| 3 | `ItemContainerOperations.RelocateAsync` | 不同型 | 能 | 有覆盖 |
| 4 | `ItemContainerOperations.SplitAsync` | 同型 | 走不到 | 有覆盖 |
| 5 | `ItemContainerOperations.SwapAsync` | 不同型 | 能 | 有覆盖 |
| 6 | `ItemContainerOperations.MergeAsync` | 同型 | 走不到 | 未测定 |
| 7 | `ItemContainerOperations.ConsumeAsync` | 同型 | 走不到 | 未测定 |
| 8 | `SelectCardCommand.DeliverAsync` | 不同型 | 不足以确定 | 未测定 |
| 9 | `PickUpGroundDropCommandHandler.CarryOverAsync` | 改前同型／改后不同型 | 能 | 有覆盖 |
| 10 | `MoveItemSpaceCommandHandler.PutOnAsync` | 不同型 | 走不到 | 未测定 |
| 11 | `MoveItemSpaceCommandHandler.TakeOffAsync` | 不同型 | 走不到 | 未测定 |
| 12 | `InventoryEntryAssembly.SaveAsync` → `UpdateOwnAsync`／`InsertOwnAsync` | 它带事实，另判 | 今天可达 | **无覆盖** |
| 13 | `CharacterRosterService` 的 `InsertOwnAsync(EquipmentCore(piece), null, …)` | 不适用 | 今天造不出 kind 8 | 未测定 |

**每处出处都标明了谁做的、是实测还是代码级读出**，举其中要紧的四处：
#2(a)／#4／#6／#7(a) 四道闸门是 **QR-09-A 自读源码核实**（出处：TASK-09「订正三」那张表）；
#2(b) 的 `RefillAsync` 那一支**只有执行者第一轮的一次探针实测**、**未经第二方复核**
（QR-09-A 的原话已逐字引在清单里，并核过与出处逐字相符）；
#8 是**完整变更评审 NB-8 自读源码核实**（原话逐字引，接排时未改一字，并在括号里注明源文本换行三次）；
#12／#13 是**本任务亲读源码**（`SaveAsync` 的脏租约扫描与 `avatarDetail` 三元；
`CharacterRosterService.EquipmentCore` 与 `CharacterCreationRuleService` 的
`private const byte EquipmentKind = 1;`）。

**结论（可从表逐行复算，复算口径写在 3.7.3）**：
同型 5 行（#2、#4、#6、#7、#9 改前）；今天能被时装走到 5 行（#1、#3、#5、#9 修后、#12）；
**两个集合在交付态上没有共同的行 → 既同型又今天能被时装走到的是 0 行**（改前是 1 行，即 #9）。
判据有无：有覆盖 5（#1、#3、#4、#5、#9）＋ 无覆盖 1（#12）＋ 未测定 7 ＝ 13 ✓。

**没有触发阻塞条件。** 未查出新的、TASK-09 未覆盖的同型缺陷。
本节把清单由 15 个 `PutAsync` 调用点扩到 13 个写入方，**扩出来的三行（#1、#12、#13）都不同型**。
唯一一个新写下的、值得单独点名的读数是 **#12**：它是这 13 行里
**唯一一个「闸门本身没有判据钉住」的写入方**——挡它的是 RV-06，而 RV-06 本轮记作无覆盖
（MU-13 那次全量报 0 失败）。**这不是新缺陷**（RV-06 那道扫描确实挡住它，本任务亲读了那段代码），
是一处**已在清单里、此前没有被排到前面**的覆盖缺口；它已被排进 3.8 的**甲档第 3 位**。

---

### 二、第 37 条的记法，与 RV-08 拆成两个制造者的前后对照

**第 37 条 ＝ 清单 3.6 的 RX-03。** 落点选在 3.6（「本轮引入并在本轮修掉的缺陷」）而不是
3.1／3.3，理由与 RX-01／RX-02 相同：它不是「本轮新增的拒绝或放行」，是一条本轮引入又本轮修掉的缺陷。
由此 3.6 由 2 条变 3 条，反向扫合计由 36 变 **37**——「第 37 条」这个说法在清单里能对上号。

**RX-03 那一行写了六样，逐样点到**：
① 制造者是谁（`PickUpGroundDropCommandHandler.CarryOverAsync` → `PutAsync` → `WriteOneAsync(…, null, …)`）；
② 引入者与放大者分开（起因 A＋B 引入形状；那句无条件换 `Value` 基线上就有，本轮没动过）；
③ 后果是什么（`Survivors` 三趟不认 → 行重建 → 回捞按新 `Core.Value` 落空 → 六列 NULL →
下一次装载抛 → `SelectCharacterHandler` 的 `catch` 关连接，**所有者看到「选完角色进不去」**）；
④ **由谁发现：完整变更评审的阻断发现 BF-1，不是执行方自查，也不是快审**，并抄下评审自造探针的读数与还原散列；
⑤ TASK-09 怎么修的（按 `landed.WorldClass` 分岔，时装只写 `Durability`，理由是
`FloorPickup.Instance` 的 `<param>` 把那个值定义成「把这件装备与另一件区分开的值」，
时装那一位是身份、也是查六列的键——**这条通路本来就把两种东西写进了同一位**）；
⑥ 修后有无覆盖（**有覆盖**：`PickedUpAvatarStaysWholeTests` 三条 ＋ 变异 MU-09-1 的
`失败: 2，通过: 1` 与两条红的读数）。
另附一处**明写不足以确定**：一件时装今天能不能真的出现在地板上，至今未确立。

**RV-08 的前后对照**

| | 第二版（原文，保留在清单里） | 第三版 |
|---|---|---|
| 「判据有无」一栏 | 只有两个字：**有覆盖** | **拒绝本身有覆盖**（按此计入 3.5 的 15）；**两个制造者分开记，见紧接表后的一小节** |
| 「凭据」一栏 | 只有 MU-5 那一次 | 不变（MU-5 仍是「这条拒绝有判据会红」的凭据），另加一小节把两个制造者各自的凭据列出 |
| 制造者 | **一个也没有枚举** | 两个，逐个给「怎么弄没的／谁发现的／今天的状态／判据有无」：制造者一＝背包内拖动（`SplitAsync` 目标格，QR-06-1 发现，TASK-08 修，MU-20 覆盖）；制造者二＝地面拾取（`CarryOverAsync`，**完整变更评审 BF-1** 发现，TASK-09 修，MU-09-1 覆盖） |
| 改写理由 | —— | 逐字引评审第八节那四行（已核对逐字相符） |

**一句话的差别**（写在清单里）：第二版写下「RV-08 有覆盖」的那一刻，
**它覆盖的只有制造者一**；制造者二当时既不在 3.1–3.4 的 34 行里、也不在 3.6 的两条里、
也不在正向扫的 B-01..B-05 里。**这不是「量少了」，是「没扫」。**

**这次拆分不改 3.5 的三个数**：RV-08 仍按「拒绝本身有覆盖」计进 15 条那一格。
3.5 第三版唯一改的是「3.6 由 2 条变 3 条」，因此本节合计由 36 变 37——这一句已写进 3.5。

---

### 三、NB-1 订正的前后对照与复算过程（CK-031）

**编号说明**：要订正的这一条由 `reviews/change-review.md` 第七节的 **NB-1** 指出，
**那是评审自己的编号**；清单第五节本来就有一条叫 NB-1 的（「抽取」与「进程内自增」分不开），
**两者不是同一件事**。为免混淆，清单把这条修正案编作 **NB-1b**，并在开头写明了这一点。

**前后对照（原文逐字保留，不是默默改数）**

| 出处 | 原文（保留） | 订正后 |
|---|---|---|
| 清单 3.3 的 RP-06 | 「第三个字段是 `Math.Max(0, int.MaxValue - now)`（**约 2.1×10⁹**）而不是落地计数」 | 就地标注：**约 3.59×10⁸ 秒，约 11.4 年**；原文写的「约 2.1×10⁹」**是错的**，修正案见第五节 NB-1b |
| 生产注释 `ItemContainers.ArrivingAvatar.NeverExpires` 的 `<remarks>` | "Nobody has yet watched a client draw an avatar whose remaining seconds are **two billion**" | 正文改为 "…whose remaining seconds are **what this bound leaves**"，并另起一段 `<para>` 以 "**Amended, and the original is kept here rather than rubbed out.**" 开头，把原句逐字引出、写明错在哪、附同一条算式 |
| `AGENTS.md` | （此前没有写过这个数） | 新增「实机须知」第 3 条与「修正案其五」，其五把上面两处原文逐字列表抄下 |

**错在哪，一句话**：两处都把 `int.MaxValue` 本身当成上线的数，
而 `GameInventoryDelivery.WriteItem` 写上去的是
`Math.Max(0, avatar!.ExpireDate - checked((int)preparedAt.ToUnixTimeSeconds()))`——**是差，不是被减数**。

**复算过程（CK-031，本任务当场跑的）**

命令：`now=$(date +%s); r=$((2147483647 - now)); awk -v r=$r 'BEGIN{printf "%.2f %.1f\n", r/31556952, r/86400}'`
读表时刻：`date -u` 给 `2026-09-06T09:32:11Z`。

```text
int.MaxValue         = 2147483647
now（unix 秒）        = 1788687136
int.MaxValue − now   = 2147483647 − 1788687136 = 358796511 秒
358796511 ÷ 86400    = 4152.7 天
358796511 ÷ 31556952 = 11.37 年     ← 分母取儒略年 365.2425 天 = 31556952 秒
```

**结果：约 3.59×10⁸ 秒 ≈ 4,152 天 ≈ 11.4 年。** 对照：**2.1×10⁹ 秒 ≈ 68 年**，差了将近六倍。
**这个数每过一秒少一**，所以清单与注释都写成「一次带时刻的减法」，任何人拿自己的钟即可复算或推翻。
（与评审给的 `≈3.6×10⁸ 秒／约 11.4 年` 相符；评审给的 `now ≈1.79×10⁹` 与本次实读 `1.7887×10⁹` 同量级。）

**为什么这一条影响所有者实机判断**（清单与 `AGENTS.md` 都写了这一段）：
所有者下一步要在实机上发一件时装、看客户端把那一格画成什么。
若他记着「约六十八年」而客户端画出十一年多，**他会去找一个并不存在的缺陷**；
若他记着「约十一年半」，同一次观测读作「这个占位在正常工作」。
反过来，**客户端若真画成 68 年、或一个不合理的日期、或拒绝渲染，那才是要追的**。

---

### 四、分档标准与分档结果（清单 3.8）

**分档标准（写死在 3.8 开头的三行表里）**。档位只问一件事：
**这一条真被触发时，所有者在实机上看到什么。** 判定依据是「这条路径失败之后由谁接住」：

- **甲**：失败发生在选角进场那一趟里（`ReadCharacterItemsAsync`／`SaveAsync`／`GameInventoryDelivery.WriteItem`），
  异常落进 `SelectCharacterHandler.HandleAsync` 的 `catch (Exception error)`，
  该分支把 `session.ConnectionId` 加进 `pendingCloses` 并把失败记进日志 → **选完角色进不去，连接被关掉**。
  （证据类型：**代码级读出，本任务亲读该 `catch` 分支**，并核过 `GameInventoryDelivery.Notifications`
  是在 `ExecuteAcceptedWorkflowAsync` 里被调、而后者就在那个 `try` 里。）
- **乙**：失败发生在一笔领域结算里，`DomainTransaction` 的 `catch` 整笔回滚 → **那次操作没生效，角色还在**。
- **丙**：不抛，只是一次请求得到另一个答案、或一行日志／一段帧字节读起来不同 → **数据不变**。

**排序规则**：先按档（甲→乙→丙），**同档之内「今天可达」排在「今天到不了」前面**。

**分档结果**：**甲 8 ＋ 乙 5 ＋ 丙 11 ＝ 24 条**（本任务用脚本数过三张表的行：8／5／11）。

- **甲档**：RV-07、RV-09、RV-06、RV-11（四条**今天可达**）；RV-18、RV-12、RV-13、RV-19（今天到不了）。
- **乙档**：NB-1（第五节那条）、RV-21、RV-22（＝E-05）、E-10、RV-14。
- **丙档**：RP-06 的另一半、A-27、RV-23、RP-08、RV-02、RV-03、RG-01、E-11（以上今天可达或未观测）；
  RV-04、RG-02、RP-09（今天到不了）。

**24 这个数怎么来的，逐项对得上 3.5**（复算写在 3.8.1）：
无覆盖 11（甲 4 ＋ 乙 1 ＋ 丙 6）＋ 未测定 9（甲 4 ＋ 乙 2 ＋ 丙 3）
＋ 3.5 点名的另三条正向扫未测定 A-27／E-10／E-11（乙 1 ＋ 丙 2；E-05 与 RV-22 是同一处，合记一条不重计）
＋ 第五节的 NB-1（乙 1）＝ 11 ＋ 9 ＋ 3 ＋ 1 ＝ **24** ✓。
**本节不改 3.5 的任何一个数**，只是把同一批条目按后果重排了一次序。

**「先看哪个」写成了一段话（3.8.2），不是让读者自己从表里挑**：
先看甲档前四条指向的同一件事——一件时装的六列在「装载 → 存盘 → 再装载」那一趟上是不是齐的
（RV-07、RV-09、RV-06、RV-11）；这四条**今天都可达，而且四条的凭据都是同一种：那次全量运行报 0 失败**，
即**今天没有一条判据钉住它们**。接着看丙档前两条（RP-06 的另一半、A-27）——时装画出来的样子，
剩余秒数应当是**约 11.4 年**。再看丙档第 3、4 条（RV-23、RP-08）——普通装备的拖动行为变了，
而且是变好，**这是本轮记在案的、有意的行为变化，不是未记的缺陷**。

---

### 五、三条检查的实际输出

#### CK-030（Non-command）：逐句核对新写下的记载

做法**不是词表搜**，是把第三版新写的每一段拿出来逐句问「这句在说一个个例，还是在说一类？」。
下面只记**改过的**，未改的不逐句复述。

| # | 起初写的 | 问题 | 改成 |
|---|---|---|---|
| 1 | 3.6 RX-03：「『一行 kind 8 而六列为空』这个形状，**在本轮之前造不出来**」 | 这是一句关于「所有写入方」的全称，而我给的理由只覆盖地面拾取一条路——**比证据强** | 收窄成「**就地面拾取这一条路而言**（这句只说这一条路）：本轮之前它写下的时装行带 kind 1，`WithAvatarDetail` 早返…」，并标证据类型为代码级读出 |
| 2 | `AGENTS.md`：「清单第一版、QR-06-1、TASK-08 执行结果、QR-08-1、D-08、清单第二版、**TASK-09 之前的历次记载**都没有点到这条门」 | 「历次记载」是读者无法证伪的全称 | 换成两条可核的观测：评审自己那句（逐字引，已核对）＋ 本任务在清单第二版上按四个词跑的一次只读检索的**实际读数**（命中 2 处，逐处读过，两处都与这条门无关，并写明是哪两处） |
| 3 | 3.7 表 #8：第五栏只写机理，没答「今天能不能被时装走到」 | 把该说的话**漏掉**了，而且会让 3.7.3 的复算对不上 | 补上「**今天能不能被时装走到：不足以确定**（一张卡的面能不能是时装，TASK-09 明写未测）」，并说明这不影响本节结论 |
| 4 | 3.7.3：「有覆盖的 4 行（#1、#3、#5、#9）……4 ＋ 1 ＋ 8 ＝ 13」 | **表是对的，小结算错了**——漏了 #4，而且 4＋1＋8 本身就不等于 13 | 改成「有覆盖 **5** 行（#1、#3、#4、#5、#9）＋ 无覆盖 1 ＋ 未测定 7 ＝ **13** ✓」，并把三条小结的复算口径逐条写明 |
| 5 | 3.7.3：「复算口径：数第四栏出现『同型』二字的行」 | 「不同型」里也有「同型」二字，按这个口径复算会得出错的数 | 改成「按整词读，不按字串数」，并把「同型／不同型／不适用」三类的行号全部列出，让读者当场核 13 ＝ 5 ＋ 8 |
| 6 | 表内引 NB-8 的原话，结尾用「」代替了源文本的句号 | 「引用他人原话逐字相符」这条不满足 | 补回句号，并在括号里注明「源文本在此处换行三次，作表内引用时接排，未改一字」 |
| 7 | `AGENTS.md` 引评审那句时写成「不在**清单第三节**的 34 行里」 | 源文本写的是「不在 **3.1–3.4** 的 34 行里」——**引错了字** | 改回逐字，并用脚本核过 |

**「有没有把该说的话压成『不足以确定』」，逐处看过，两处特意没有压**：

- **3.7 表 #12**（`SaveAsync` 那一族）：本可以写成「未测定，不足以确定」了事。
  没有那样写——它写成「**今天可达**，被 RV-06 那道扫描挡住，而 **RV-06 本身无覆盖**」，
  并在 3.7.3 与 3.8 甲档第 3 位各点一次。**这是本任务新写下的、对所有者最有用的一句。**
- **3.8.2「先看哪个」**：本可以只给三张表让读者自己挑。
  没有那样写——它把「先看甲档那四条、它们今天都可达且今天没有一条判据钉住」直接写成了一段话。

**引用核对（脚本核过，不是目测）**：第三版在清单里新加的 **4 处 blockquote** 逐行与出处
（`reviews/change-review.md`、TASK-09 文件）**逐字相符**；表内接排的那一处 NB-8 引文
在去掉换行后与源文本**逐字相符**；`AGENTS.md` 里引评审的那一句同样核过。

**未做的、也不该由本任务做的（明写，免得读者以为漏了）**：
评审的 NB-2（清单说 `git status` 是 18 行、交付态实为 19 行）、NB-3（起因 A 漏列一处 `ItemKind` 读点）、
NB-4（`AGENTS.md` 讲普通装备那句「那不是本轮新引入的缺陷」措辞会误导）、NB-5（邮箱附件摘要未点名为残余）
**四条都不在本任务的五条验收条件里**，本任务**没有动它们**，按「不要自行扩范围」办。
（其中 NB-4 那句建议的改法，本任务在清单 3.8.2 里按评审的措辞写了一遍：
「这是本轮记在案的、有意的行为变化，不是未记的缺陷」；`AGENTS.md` 那句原文未改。）

#### CK-031（Non-command）：复算「剩余秒数」

算式与结果见上面第三节。**读数：`2147483647 − 1788687136 = 358796511` 秒 ＝ 4,152.7 天 ＝ 11.37 年。**
读者可自行复算：`echo $((2147483647 - $(date +%s)))`。

#### CK-032：全量判据

```text
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo

已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   524，已跳过:     0，总计:   524 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   320，已跳过:     0，总计:   320 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   236，已跳过:     2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:  1039，已跳过:     0，总计:  1039 - Dfo.Server.Application.Tests.dll (net10.0)
EXIT=0
```

合计 **524 ＋ 52 ＋ 60 ＋ 6 ＋ 320 ＋ 236 ＋ 1039 ＝ 2237 通过 / 2 跳过 / 0 失败，EXIT=0**，
与契约给的 **2237 / 2 / 0（524/52/60/6/320/236+2/1039）逐项相同**。
本任务不改可执行代码，这个读数应当与 TASK-09 结束时相同——**确实相同**。
本次未出现既知偶发 `ObjectDisposedException: 'SQLitePCL.sqlite3'`。

---

### 六、自查：逐条对照验收条件 1..5

| 条 | 要求 | 达成 | 落点与凭据 |
|---|---|---|---|
| 1 | 清单新增一节「制造者扫」，枚举**写入方**（不是判定点），对每个答「能否产生一行 kind 8 六列为空／有没有判据覆盖」，**且能独立于判定点扫被读懂** | **达成** | 清单 3.7 节。枚举口径三条（两条本任务亲跑 grep、一条注明沿用 TASK-09 未重跑）；13 个写入方逐个作答，两栏分别答「能否产生该形状」与「判据有无」；开头自带问题界定与三步机理，不要求先读 3.1–3.6。行数 11＋2＝13 由脚本核过 |
| 2 | 第 37 条记进清单，含制造者／后果／**由谁发现（完整变更评审，不是执行方自查）**／TASK-09 怎么修／修后有无覆盖；并把 RV-08 两个制造者分开写 | **达成** | 清单 3.6 的 **RX-03**（六样齐，发现者写明是完整变更评审的 BF-1，并抄了评审自造探针的读数与还原散列）；3.1 表后新增「RV-08 的两个制造者」，含第二版原文的保留与前后对照。反向扫合计 36→37，三处计数同步改过 |
| 3 | 订正 NB-1 的算术，**按仓规补记修正案并写明原文**，先自己算一遍并把算式写出来，并写清它对所有者实机判断的影响 | **达成** | 清单第五节 **NB-1b**（原文两处逐字保留＋算式＋影响一段）＋ RP-06 就地标注 ＋ 生产注释 `ArrivingAvatar.NeverExpires` 的 `<remarks>`（原句逐字引在 Amended 段里）＋ `AGENTS.md` 修正案其五与实机须知第 3 条。算式：`2147483647 − 1788687136 = 358796511` 秒 ≈ 11.4 年 |
| 4 | 把「未测定／无覆盖」按后果分档，让读者知道**先看哪个**，**分档标准要写明** | **达成** | 清单 3.8 节。分档标准写成一张三行表，每档的判定依据是「失败之后由谁接住」，甲档那一条由本任务亲读 `SelectCharacterHandler` 的 `catch` 分支坐实；排序规则另写一行。结果 甲 8／乙 5／丙 11 ＝ 24，复算在 3.8.1，「先看哪个」写成 3.8.2 一段话 |
| 5 | `AGENTS.md` 相应段落同步更新，含第 37 条与订正后的秒数 | **达成** | 「做成了三件事」→「四件事」并新增第 4 件（地面拾取那一整条，含它暴露的方法缺口与 3.7 节的指路）；「实机须知」由两条变三条，第 3 条是订正后的秒数；「补记的四条修正案」→「五条」，其五是秒数订正 |

**硬约束逐条**：
未改动任何可执行代码或判据（BD-011 只改注释；改动区间 68–105 行内非 `///` 的行只有一行空行与
`private const int NeverExpires = int.MaxValue;`，两行都未被触及；全量计数不变可佐证）；
**未使用任何 git 还原类命令**；未提交、未建检查点；
不写读者无法证伪的基数（新写下的每个数都带口径或算式：13＝11＋2、24＝8＋5＋11、
13＝5＋1＋7、358796511＝2147483647−1788687136）；按成员名索引，
新写下的位置全部按成员名给（`ItemContainers.cs` 的三个行号只出现在**枚举口径**那一句里，
用来说明那次 grep 读到哪三行，不作为索引）。

**阻塞条件**：未触发。「制造者扫」未查出新的、TASK-09 未覆盖的同型缺陷；
唯一新点名的读数（#12 的闸门 RV-06 无覆盖）**不是缺陷**，是已在清单里的覆盖缺口被排到了甲档第 3 位。

---

## Independent Quick Review — QR-10-A

**判定：`changes_required`。**

**被评审的契约版本**：`## Task Unit Contract`，Contract revision 1，无修正案。
**被评审的 owned diff**：工作副本相对基线 `565d33fe`，`git status --porcelain` **21 行
＝ 14 个 ` M` ＋ 7 个 `??`**（本次亲自复读，与执行结果所记逐行相同）。未提交，无检查点。
**评审边界**：只读。本轮**没有改动任何文件**，没有做变异，没有用任何 git 还原类命令，
没有触碰主检出与 `.worktrees/client-debug-lab`。

### 一、逐项核实的读数

**1. `ItemContainers.cs` 本轮是不是只改了注释——是。** 三条互相独立的证据：

- **区间内的非注释行未被触及**：`awk` 取第 68–105 行、再滤掉 `///` 开头的行，本轮亲跑，
  读出**两行**——一行空行与 `private const int NeverExpires = int.MaxValue;`，
  与执行结果所记相同；后者与基线上 TASK-02 引入的那一行逐字相同。
- **相对基线的全部非注释差异，逐行都不是本任务的题目**：
  `git diff 565d33fe -- <文件>` 里滤掉 `///` 行之后，读出 **44 行**（增删合计），
  逐行读过，全部落在三处：`ArrivingAvatar` 类体（18 行）、
  `PutAsync` 改为转调 `WriteOneAsync` 与 `WriteOneAsync` 本身（含 `StoredItem` 初始化，18 行）、
  `LandAsync` 的 `IntoEmptySlot` 分岔（8 行）。**没有一行与「剩余秒数」有关。**
- **这 44 行在 TASK-10 之前就已是这个样子，有独立的持久记载为证**：
  `reviews/change-review.md`（完整变更评审，写于 TASK-10 之前）第 241–242 行写
  「**`PutAsync` 的语义**：由 `async ValueTask` 改为转调 `WriteOneAsync` 的表达式体，
  `ArgumentNullException.ThrowIfNull(core)` 随之移进 `WriteOneAsync`（仍是 `async`）」；
  第 44 行写 `WriteOneAsync(slotIndex, core, null, cancellationToken)`；
  第 283 行写 `LandAsync` 用 `itemKind == ArrivingAvatar.ItemKind`。
  三处与今天的工作副本逐字相符。

  **散列**：`ItemContainers.cs` ＝ `be6380973bdcc9a1cd8b4fd7210f992cf7ee504ae53968e546f4bcd66955c4d6`，
  与执行结果所记相同。**字节锚**：`SelectCharacterAcceptedTranscriptTests.cs` ＝
  `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`、
  `GroundPickupSlice.cs` ＝ `cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d`，两者均与所记相同。

  **结论：BD-011「仅限该处措辞订正，不得改任何可执行代码」达成。**

**2. 秒数复算——对。** 本轮自算：`2147483647 − 1788687136 = 358796511` 秒；
`358796511 ÷ 86400 = 4152.74` 天；`÷ 31556952 = 11.369` 年。与所记 `4152.7`／`11.37` 相符。
对照 `2.1×10⁹ ÷ 31556952 = 66.5` 年（清单写「≈ 68 年」，那是就 `int.MaxValue` 本身而言的
`2.147×10⁹ ÷ 31556952 = 68.0` 年——两个数在同一句里被混着用，见下面缺陷 F-2）。
「差了将近六倍」＝ `2.1×10⁹ ÷ 3.588×10⁸ = 5.85`，成立。
**原文两处逐字保留**：清单第五节 NB-1b 抄下 RP-06 的「约 2.1×10⁹」与生产注释的
"remaining seconds are two billion"；生产注释另起 `<para>` 以
"Amended, and the original is kept here rather than rubbed out." 开头把原句引出。
**两处都附了算式**（清单第五节、生产注释 `<para>` 内、`AGENTS.md` 修正案其五各一遍）。
**仓规「补记修正案而非默默改数」达成。**

**3. 「制造者扫」的口径与复算——数对，口径有一处漏（见 F-1）。**
本轮亲跑的只读检索：

- `grep -rn "WriteOneAsync" Server/src --include=*.cs` → **3 行**（`ItemContainers.cs:395/404/506`），
  与所记相同。
- **`WriteOneAsync` 确实是容器一族的唯一汇流口，这一条本轮独立坐实**：
  在 `Server/src` 下检索 `.Write(` 的调用，唯一一处写 `StoredItem` 的是
  `ItemContainers.cs:413`，而它就在 `WriteOneAsync` 体内。
- `grep -rn "PutAsync" Server/src --include=*.cs` → **21 行**；本轮逐行判过，非调用点 **6 行**
  （`IItemContainer.cs:155` 声明、`:157` `<see cref>`、`ItemContainers.cs:393` 实现、`:400` `<see cref>`、
  `ItemPlacementRuleService.cs:36` `<c>`、`GroundPickupSlice.cs:405` `<c>`），余 **15 个调用点**，
  按宿主成员去重得 **10 个**，与 TASK-09 第三节的名单逐个对得上。
- `grep -rn "InsertOwnAsync\|UpdateOwnAsync" Server/src --include=*.cs` → 生产调用方两处：
  `CharacterRosterService.cs:219` 与 `InventoryEntryAssembly.cs:187/195`，与所记相同。

  **行数复算**：10 个成员 ＋ `LandAsync` 的第二支 ＝ **11**；入场存盘 **2**；**11 ＋ 2 ＝ 13**。
  两张表本轮亲数，行数为 11 与 2。✓
  **两个集合的交集**：同型 5 行（#2、#4、#6、#7、#9 改前）与今天能被时装走到 5 行
  （#1、#3、#5、#9 修后、#12），逐行比对，**交集 0 行**，与所记相同。✓
  判据有无 **5 ＋ 1 ＋ 7 ＝ 13** ✓。
  **阻塞条件未触发**：本轮未查出新的、TASK-09 未覆盖的同型缺陷。

**4. 分档——数对，甲档依据成立。**
甲 8 ＋ 乙 5 ＋ 丙 11 ＝ **24**，本轮逐行数过三张表，行数为 8／5／11。✓
来历复算逐项核过：无覆盖 11（甲 4 ＋ 乙 1 ＋ 丙 6）＋ 未测定 9（甲 4 ＋ 乙 2 ＋ 丙 3）
＋ 正向扫三条（乙 1 ＋ 丙 2）＋ 第五节 NB-1（乙 1）＝ **24** ✓，并与 3.5 的三个数逐条对得上。
**甲档依据本轮亲读源码坐实**：`SelectCharacterHandler.cs` 第 145 行 `catch (Exception error)`
在 `session is not null` 时 `pendingCloses.Add(session.ConnectionId)`（第 150 行），
第 197 行的 `foreach (var connectionId in pendingCloses.Distinct())` 逐个 `closer.CloseAsync`；
`ExecuteAcceptedWorkflowAsync`（第 213 行定义）在第 106 行被调，**就在第 90 行那个 `try` 里**，
而 `GameInventoryDelivery.Notifications` 在第 248 行、即该方法体内；
入场装载所走的 `PrepareSelectedCharacterSessionCommand` 也在同一个 `try` 内（第 92–99 行）。
**「触发即选完角色进不去」成立。**
**可用性**：3.8.2 把「先看哪个」写成了一段可照做的话（甲档前四条→丙档前两条→丙档 3、4 条），
并点明前四条「今天都可达且今天没有一条判据钉住」。**对要做实机验证的读者是可用的**，
符合评审「所有者要的是『先看哪个』」。

**5. `#12` 那个「唯一」——在表的词汇下成立，在字面读法下过强（见 F-3）。**

**6. 引文出处——核过的四处都逐字相符。**
`reviews/change-review.md` 第八节建议 1、2（清单 3.7／3.8 开头两处 blockquote）逐字相符
（建议 1 的第三句「本轮若做了这一趟……」被截去，未加省略号，属截取而非改字）；
NB-8（评审第 288–290 行）接排引用逐字相符；QR-09-A「我没有重跑那个探针……
本评审对那六条读数不足以确定」（TASK-09 第 491 行）逐字相符；
`AGENTS.md` 引评审的「这条新增的制造者不在 3.1–3.4 的 34 行里、不在 3.6 的两条里、
不在正向扫的 B-01..B-05 里，`AGENTS.md` 也没有点到它」与评审第 81–82 行逐字相符。
**执行者自陈第四次栽在「表是对的、小结算错了」，本轮核过订正干净**：
3.7.3 现写「有覆盖 **5** 行（#1、#3、#4、#5、#9）＋ 无覆盖 1 ＋ 未测定 7 ＝ **13**」，
与 3.7.1／3.7.2 两张表第七栏逐行相符，且复算口径改成「按整词读，不按字串数」并列全行号。
反向扫合计 34 ＋ 3 ＝ **37**：本轮亲数 3.1／3.2／3.3／3.4 ＝ 19／4／9／2，3.6 ＝ 3 条（RX-01/02/03）。✓
验收条件 2 的六样在 RX-03 那一行齐备，发现者写明是**完整变更评审的 BF-1**，
MU-09-1 的读数（`失败: 2，通过: 1` 与两条红）与 TASK-09 第 453–456 行逐字相符。

**7. 全量判据（CK-032）——本轮自己重跑，读数相同。**

```text
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
524 + 52 + 60 + 6 + 320 + 236 + 1039 = 2237 通过 / 2 跳过 / 0 失败，EXIT=0
```

与契约给的 2237 / 2 / 0（524/52/60/6/320/236+2/1039）逐项相同。未出现既知偶发。

### 二、须整改的缺陷

**F-1（阻断）——「制造者扫」的枚举口径漏了 `Server/src` 里的第三族写入方。**

清单 3.7 口径一写：「『写入方』＝ 一个能把一行物品核心交到世界表 `items` 上的**生产成员**……
今天读到**两族**」，并把排除规则写成「`Server/tests/` 下的测试替身不算生产写入方」。
按这个口径本轮在 `Server/src/Dfo.Server.Persistence` 下检索 `INSERT INTO` 与 `UPDATE ... items`，
读出**第三族**：

- `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:434`
  `SqliteWorldItemTable.InsertAsync(InventoryItemCoreState core, AvatarDetailRow? avatar, …)`
  → `INSERT INTO items(item_core, avatar_expire_date, …)`（第 445 行），`avatar` 为 null 时
  六列由 `BindAvatarColumns` 绑成 NULL；
- 同文件 `:544` `SqliteWorldItemTable.UpdateAsync(…, AvatarDetailRow? avatar, …)`
  → `UPDATE items SET item_core=@core, avatar_expire_date=@expire, …`（第 552 行），同上。

**这两个成员在 `Server/src` 内、不在 `Server/tests/` 下，按口径一的字面读法就是「生产成员」，
而且它们写的正是本节盯的那个形状（kind 8 的 core ＋ 六列 NULL）。它们不在那 13 行里，
也没有被 3.7 末尾「这个枚举口径挡不住什么」六条中的任何一条排除掉。**

**这不是一个活的制造者**，本轮把可达性查清了并记在这里：
`grep -rn "IWorldItemTable" Server/src --include=*.cs` 只读出接口声明与实现两处，
`grep -rn "SqliteWorldItemTable" Server/` 在 `Server/src` 下零调用方，
只有 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/WorldAndSlotTableTests.cs:455` 与
`Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/CreatureIdentityFitsTheWireTests.cs:55/107`
三处直接构造。**所以 3.7 的实质结论（交集 0、没有新的同型缺陷、#12 的读数）全部不受影响，
本轮也未因此发现新缺陷**——阻塞条件仍然未触发。

**为什么仍记为须整改**：3.7 存在的理由正是「上一趟扫描的对象选漏了」。
一份写入方清单，其**自述口径**能被读者用一条 `grep` 在同一个目录里证伪，
就重演了它要修的那个缺陷类型；而且这两个成员一旦被接上（今天没有调用方，明天可能有），
写下的就是无闸门的那个形状。
**整改（只动记载，落在 BD-006 内）**：在 3.7 里把这一族**写进去**——
或作第 14／15 行、判据栏记「今天零生产调用方」，或作口径一后的一条明写排除，
并把「今天读到两族」改成一句可复核的话（例如「今天读到三族，其中第三族在 `Server/src` 下
零调用方，出处：上面那两条 grep」）。相应地复述一次 13 这个数的口径。
**不必也不应改任何可执行代码。**

**F-2（须整改，同一处记载的内部不一致）——「11.4 年」与「十一年半」两个数并存。**
复算给的是 `11.37` 年（≈ 十一年四个半月）。清单 NB-1b 标题与 3.8.2 写「约 **11.4** 年」，
但同节的所有者段落写「若他记着『约**十一年半**』」；`AGENTS.md`「实机须知」第 3 条标题写
「应当画出约**十一年半**」而正文写「≈ **11.4** 年」；生产注释 `<para>` 两处写
"roughly eleven and a half years"／"about eleven and a half"。
本任务的整个题目就是「一个由算术得出的数被写下时没有把算式一并写出来」，
而 NB-1b 自己引的仓规是「各项、运算、结果，三样齐」。三份交付物在同一个数上给出两种说法，
**这正是它要修的那类缺陷的小号复制**。
（同段「2.1×10⁹ 秒 ≈ 68 年」也混了两个被减数：`2.1×10⁹` 是 66.5 年，`2.147×10⁹` 才是 68.0 年。
这一处不必改数，但宜写明 68 年对应的是 `int.MaxValue` 本身。）
**整改**：三处统一到复算给出的那个数（「约 11.4 年」或「约十一年四个月」），
凡出现该数处保持一致。算式已在，不必重算。

**F-3（须整改，记法过强）——3.7.3 那句「唯一」按字面读比证据强。**
3.7.3 写「**#12 是这 13 行里唯一一个『闸门本身没有判据钉住』的写入方**（挡它的 RV-06 记作无覆盖）」。
按清单第〇节写死的词汇，「无覆盖」＝ 施过变异且该次运行没有判据变红，「未测定」＝ 没施过变异；
表第七栏里记「无覆盖」的确实只有 #12 一行（本轮亲数：5 ＋ 1 ＋ 7）。
**但表里还有 7 行记「未测定」，它们的闸门是「没有量过」，不是「量过、钉住了」。**
「没有判据钉住」这个说法按字面覆盖后者，读者会读成「另外 12 行的闸门都有判据钉住」。
3.7.1 的 #12 单元格用的是「唯一一个『闸门本身**无覆盖**』」——那句是可从表机械复核的，没有问题。
**整改**：把 3.7.3 那句改用同一个词（「唯一一个记作**无覆盖**的」），
或补一句「其余 7 行记『未测定』，即它们的闸门有没有判据钉住**尚未测定**」。
（执行结果第一节与第五节 CK-030 里同一句话有同样问题，但那两处是 Task 文件、不是交付物，
按本次尺度只记不判。）

### 三、未判的

- 清单 3.7「挡不住什么」第 2 条所依赖的 refill 探针读数、第 3 条的两个时装模板读数、
  第 4 条的奖励卡面——三处都由记载自己明写为不足以确定，本轮**未重跑**（会新增文件，超出只读边界），
  **本评审对这三处读数不足以确定**。
- `AGENTS.md` 里「在清单**第二版**上按四个词跑过一次只读检索，命中 2 处」——
  第二版已被第三版覆盖，**本评审无法复核这条读数**；它带了检索词与逐处说明，是可证伪的写法。
- 评审 NB-2..NB-5 四条不在本任务五条验收条件内，执行结果已明写未动，**本次不判**。

### 四、结论

契约五条验收条件与三条检查**实质达成**，硬约束（不动可执行代码、不提交、不建检查点、
不用 git 还原类命令）**逐条核过、均未违反**，全量判据本轮重跑读数相同。
须整改的三条**全部只动记载**：F-1 补一族写入方与口径措辞（BD-006），
F-2 统一「11.4 年」这个数（BD-006／BD-007／BD-011 的注释措辞），
F-3 一句记法收窄（BD-006）。**整改不涉及任何可执行代码。**


## Controller Disposition D-10 — `task-rework`，三条全部只动记载

**判定**：`task-rework`。TASK-10 保持 `in_progress`。
契约五条验收与三条检查的**实质**已由 QR-10-A 独立核实达成，硬约束零违反，
`ItemContainers.cs` 确为只改注释（评审给了三条独立证据，含一条决定性的——
`reviews/change-review.md` 写于 TASK-10 之前，其中已逐字描述今天的
`PutAsync`/`WriteOneAsync` 语义）。**三条整改全部只动记载，不碰任何可执行代码。**

### F-1（阻断）：口径漏了一族写入方

`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs` 的
`SqliteWorldItemTable.InsertAsync` 与 `.UpdateAsync` 也往 `items` 写 `item_core`，
且 `avatar` 为 null 时把六列绑成 NULL——**正是 3.7 盯的那个形状**。
它们在 `Server/src` 内，按 3.7 自述的口径**字面就是生产成员**，
却既不在 13 行里、也不在「挡不住什么」六条排除里。

**但它在 `Server/src` 零调用方**（`IWorldItemTable` 只有声明与实现两处引用，
只有两个测试文件直接构造）。因此 3.7 的实质结论——交集 0、无新同型缺陷、#12 那条读数——
**全不受影响，阻塞条件未触发**。

**整改**：补一条**明写的排除**（含「零调用方」这个理由与它的可复核依据），
并把「今天读到两族」改成一句**可被复核的话**——
即写清口径覆盖到什么、排除了什么、为什么。

**一条值得连带记下的**：这个类正是**上一个成果**在自己的已知缺陷清单里
记作「有意留下的守卫缺口，生产零调用方、零 DI 注册」的那一个。
它今天仍是那个状态。**记的时候把这条渊源写上**，
使读者知道这不是本轮新发现的东西，也不是本轮该修的东西。

### F-2：同一个数在三份交付物里写成两个说法

既写「约 **11.4** 年」又写「约**十一年半**」／"eleven and a half"，而复算值是 **11.37**。
**统一成一个说法**，并让它能从算式当场复算出来。

**这一条要在记载里点破它的讽刺**：本任务的存在就是为了修「同一个数被写成两个说法」
（NB-1 那处 2.1×10⁹ vs 3.6×10⁸），而它自己在交付里犯了小号的同一件事。
**这是本成果第五次「表是对的、小结/复述算错了」。**

### F-3：一处全称按字面盖得太宽

3.7.3 的「唯一一个『闸门本身没有判据钉住』」按字面覆盖了 7 行「未测定」。
**改用表里自己的词「无覆盖」**，并把限定域写清。

### 不要重做的

QR-10-A 已独立核实：`ItemContainers.cs` 只改注释（三条证据）；
`WriteOneAsync` 确是容器一族唯一汇流口（`Server/src` 下写 `StoredItem` 的 `.Write(`
只有一处，就在其体内）；15 调用点 / 10 成员；11＋2＝13；同型 5 与今天可走到 5 的交集为 0；
秒数 358796511 秒 = 4152.74 天 = 11.369 年，原文两处逐字保留且三处附算式；
分档 8＋5＋11＝24 与 3.5 的 11＋9＋3＋1 逐项对得上，甲档依据亲读源码坐实；
四处引文逐字相符；执行者自陈的第四次「小结算错」已订正干净；
全量 2237/2/0 EXIT=0 亲测复现。


## 执行结果（TASK-10，第二轮：补一族写入方并统一一个数）

本节是**追加**。上面的 `## Task Unit Contract`、`## 执行结果`、
`## Independent Quick Review — QR-10-A`、`## Controller Disposition D-10` 四节
**一字未删未改**。

**本轮只动散文。没有改动任何可执行代码或判据。** 生产侧唯一动过的仍是
`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` 里
`ArrivingAvatar.NeverExpires` 的 `<remarks>`（BD-011，只改注释措辞）。
**未提交、未建检查点、未使用任何 git 还原类命令，未触碰主检出与 `.worktrees/client-debug-lab`。**

### 一、F-1 的前后对照：把第三族写入方明写排除，并记下它的渊源

**改的是清单 3.7 的枚举口径（BD-006），外加 `AGENTS.md` 里复述那条口径的一段（BD-007）。**

**改前（清单 3.7 口径一，原文）**

> - **口径一**：「写入方」＝ 一个能把一行物品核心交到世界表 `items` 上的**生产成员**。
>   本任务在 `Server/src` 下按写入函数名检索，今天读到**两族**：容器一族经
>   `StoreBackedItemContainer.WriteOneAsync`；入场存盘一族经 `ICharacterItemTable` 的
>   `InsertOwnAsync`／`UpdateOwnAsync`（……）。

**改后（要点，全文见清单）**

- 口径一改写成「今天读到**三族**」，三族逐个点名，并写明**下表只列前两族的 13 行**，
  第三族（`IWorldItemTable` 的 `SqliteWorldItemTable.InsertAsync`／`UpdateAsync`）
  **被明写排除**，理由与依据另起一段。
- 口径一末尾补一句**可被复核的口径话**：这条口径覆盖 `Server/src` 下能写 `items`
  且**今天在 `Server/src` 里有生产调用方**的成员；`Server/tests/` 下的测试替身不算，
  `Server/src` 下**只被测试构造、生产无人调用**的成员也不算，
  **后者今天只有世界表一族一处，已逐条点名并给出核法**。
- 口径三之后新增一段 **「明写的排除：世界表一族 `SqliteWorldItemTable`，今天生产零调用方」**，
  内含：它写什么（`item_core` ＋ 六列，六列由 `BindAvatarColumns` 在 `avatar is null` 时
  逐列绑成 `DBNull.Value`——**正是本节盯的那个形状**）、三条 grep 的读数、结论、
  **这条排除的有效期**（到有人第一次给它接线为止；接上当天这份清单就要变成 15 行）、
  以及**渊源**。
- 「这个枚举口径挡不住什么」由六条变**七条**，新增第 7 条：
  **排除的理由只是「今天零调用方」，不是「它写不出那个形状」**；它写得出。
- 3.7.3 之外不动任何数：**13 这个数没有变**，因为被排除的那一族不进表。

**`AGENTS.md` 那一段的前后对照**

| | 原文 | 改后 |
|---|---|---|
| 复述 3.7 口径的那句 | 「按一条可复核的枚举口径（世界表 `items` 今天的**两族**写入方：……）把 **13 个写入方**逐个判过」 | 拆成两段：一段照旧写 13 个写入方与那一节问的问题；另起一段写明**三族、排除哪一族、为什么、核法、渊源、有效期**，并补一句「被排除的那一族今天走不到，**加不进那三条小结的任何一个集合，结论不因它而变**」 |

### 二、F-1 那条排除的可复核依据（本轮亲跑的核实过程与结果）

**跑的是三条只读检索，读数照抄如下**（未新增文件、未改任何文件即可重跑）：

```text
$ grep -rn "IWorldItemTable" Server/src --include=*.cs
Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs:189:public interface IWorldItemTable
Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:417:internal sealed class SqliteWorldItemTable(SqliteDatabase database) : IWorldItemTable
→ 2 行：一行接口声明、一行实现的类头。没有第三行。

$ grep -rn "SqliteWorldItemTable" Server/ --include=*.cs
Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:417   （实现的类头，它自己）
Server/tests/Dfo.Server.Persistence.Tests/Inventory/WorldAndSlotTableTests.cs:455
Server/tests/Dfo.Server.Persistence.Tests/Inventory/WorldAndSlotTableTests.cs:463
Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/CreatureIdentityFitsTheWireTests.cs:55
Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/CreatureIdentityFitsTheWireTests.cs:107
→ Server/src 下 1 行且是它自己；把它 new 出来的只有两个测试文件。

$ grep -rn "ItemTable\|ItemDomainStore\|ItemSlotTable" Server/src/Dfo.Server.Persistence/PersistenceModule.cs
:74  services.TryAddSingleton<ICharacterItemTable>(
:76  services.TryAddSingleton<IAccountCargoItemTable>(
:158 services.TryAddSingleton(_ => new SqliteItemDomainStore(databasePath));
:162 services.TryAddSingleton<IItemDomainStore>(
→ 没有 IWorldItemTable，也没有 SqliteWorldItemTable。
```

**另外亲读了那两个成员的源码，坐实「它写得出那个形状」这半句**：
`InventoryItemTables.cs:434` 起的 `SqliteWorldItemTable.InsertAsync(InventoryItemCoreState core,
AvatarDetailRow? avatar, …)` 写 `INSERT INTO items(item_core, avatar_expire_date, …)`；
`:544` 起的 `UpdateAsync(int itemUid, InventoryItemCoreState core, AvatarDetailRow? avatar, …)`
写 `UPDATE items SET item_core=@core, avatar_expire_date=@expire, …`；两者都调同文件 `:603` 的
`BindAvatarColumns`，该方法六句都是 `parameters.AddWithValue("@x", avatar is null ? DBNull.Value : …)`。

**结论（只就这四处读数而言）**：这两个成员**能**写出「kind 8 的 core ＋ 六列 NULL」，
但**今天在 `Server/src` 里没有一处按名字取用它们，也没有一处把这个类注册进容器**，
因此今天没有一条生产路径能走到它们。**评审 F-1 里「零调用方」这一条，本轮自核，成立。**
**这条读数会随代码变化，不是永久事实**——上面四条命令是它的证伪方法。

**渊源（本轮查证，并写进了清单与 `AGENTS.md`）**：它不是本轮新发现的。
本仓 `AGENTS.md`「已知缺陷与覆盖缺口」一节早已记着它，那一节由**上一个成果
`items-own-their-identity`** 写下，原话（逐字，截到「零 DI 注册」为止）：

> **今天仍不被覆盖的写入方只剩一个**：`SqliteWorldItemTable`（`InsertAsync`／`UpdateAsync`），
> 它写 `items` 时装列却没有自己的条件，退役前靠 `idx_items_avatar_uid` 抛错。
> 它**生产里零调用方、零 DI 注册**

同节把处置写作「**这个缺口今天到不了生产，是有意留下的**——去补它等于在一个生产无人调用的类上
引入新行为」；该成果的 `.rks-agent/tasks/items-own-their-identity/spec-result.md`
「Successor Guidance」第 4 条同样记着「**`SqliteWorldItemTable` 有一个有意留下的守卫缺口**
（生产零调用方、零 DI 注册）。谁第一次给它接线，谁要补那道时装唯一性守卫。」
**这两处都写在本轮开工基线之前**：`git diff 565d33fe -- AGENTS.md` 里没有一行动到
`SqliteWorldItemTable`（本轮亲跑）。**所以它今天仍是那个状态，本轮既未改它，也未把它列为新缺陷。**

**对 3.7 实质结论的影响：没有。** 三条小结（同型 5 行、今天能被时装走到 5 行、交集 0 行）
与「没有查出新的、TASK-09 未覆盖的同型缺陷」都是**就那 13 行说的**，
被排除的这一族今天走不到，加不进任何一个集合。**阻塞条件仍未触发。**

### 三、F-2 的前后对照：统一成一个说法，并让它能当场复算

**改前，同一个数在三份交付物里有两个说法**（原文逐处）：

| 出处 | 原文 |
|---|---|
| 清单 NB-1b 标题、清单 3.8.2 | 「约 **11.4** 年」 |
| 清单 NB-1b 所有者段落 | 「若他记着「约**十一年半**」」 |
| `AGENTS.md`「实机须知」第 3 条标题 | 「应当画出约**十一年半**，不是六十八年」 |
| `AGENTS.md` 同条正文、修正案其五 | 「≈ **11.4** 年」 |
| 生产注释 `<remarks>` 的两个 `<para>` | "roughly eleven and a half years"／"about eleven and a half" |

**改后：只有一个说法——「约 11.4 年」，英文侧 "about 11.4 years"。**

**统一后的说法与它的算式（三份交付物里各写一遍，读者可当场复算）**

```text
int.MaxValue          = 2147483647
now（unix 秒）         = 1788687136        ← 2026-09-06T09:32:11Z，`date +%s`
int.MaxValue − now    = 2147483647 − 1788687136 = 358796511 秒
358796511 ÷ 86400     = 4152.7 天
358796511 ÷ 31556952  = 11.37 年           ← 分母取儒略年 365.2425 天
```

**「约 11.4 年」＝ 上面末行 `11.37` 取三位有效数字。**
「十一年半」是 `11.5`，与 `11.37` 差约 **0.13 年（0.13 × 365.2425 ≈ 47 天）**——
**这就是两个说法必须并成一个的理由**。

**顺带把「68 年」对应到哪个被减数写清了**（三处同改，因为原来那句把两个数混着用）：

```text
2147483647 ÷ 31556952  = 68.05 年   ← 「约 68 年」说的是 int.MaxValue 本身
2.1×10⁹   ÷ 31556952  = 66.55 年    ← 写下的那两位有效数字本身
2147483647 ÷ 358796511 = 5.99       ← 「差了将近六倍」的那个倍数
```

**逐处落点**：清单 NB-1b（新增「只有一个说法」一句 ＋ 68 年归属 ＋ 所有者两条 bullet 改数）、
清单 3.8.2（把那个数连算式一起写出）、`AGENTS.md` 实机须知第 3 条（标题改数 ＋ 明写
「不要写成十一年半」＋ 68 年归属）、`AGENTS.md` 修正案其五（68 年归属）、
生产注释 `<remarks>`（`<para>` 改写并新增一段「只用一个说法」，**只改注释**）。
**原文两处（清单 RP-06 的「约 2.1×10⁹」、生产注释的 "two billion"）仍逐字保留，没有擦掉。**

**点破这处讽刺（已写进清单 NB-1b 与 `AGENTS.md` 修正案其五，不只写在本文件）**：
本任务存在的理由，就是修「**同一个数被写成两个说法**」——NB-1 那处 `2.1×10⁹` 与 `3.6×10⁸`。
而这条修正案第一次交付时，**把订正后的那个数也写成了两个说法**，
在三份交付物里同时留下「11.4」与「十一年半」。**这是它要修的那件事的小号复制。**
**这是本成果第五次栽在「表是对的、小结／复述算错了」上**——前四次由执行方自陈并已订正
（见清单第四节与 3.7.3 的复算口径），这是第五次，由 QR-10-A 的 F-2 指出。
**由此多记一条规矩**（已写进 `AGENTS.md`）：**一个数被订正之后，它的每一处复述都要照着算式抄，
不要用口语约数替换**——「十一年半」「六十八年」这类写法，正是让原错传开的那种写法。

### 四、F-3 的前后对照：换成表里自己的词，并写清限定域

**改前（清单 3.7.3 末条，原文）**

> **#12 是这 13 行里唯一一个「闸门本身没有判据钉住」的写入方**（挡它的 RV-06 记作无覆盖）。

**改后**

> **#12 是这 13 行里唯一一个第七栏记「无覆盖」的写入方**（挡它的 RV-06 记作无覆盖）。
> **这句话只在第〇节写死的那个词汇下成立，限定域要写清**：按第〇节，「无覆盖」＝
> **施过一次具名变异，而那次运行报 0 失败、或变红的判据里没有一条走这条路径**；
> 「未测定」＝ **本轮没有对该处施加变异**。
> 所以这一句说的是「量过、而没量出覆盖的只有 #12 一处」，
> **不是**「另外 12 行的闸门都有判据钉住」——
> 另外那 7 行记「未测定」，它们的闸门**有没有判据钉住尚未测定**，
> 今天不足以确定它们是有覆盖还是无覆盖。

**为什么这么改**：「没有判据钉住」按字面覆盖了那 7 行「未测定」，
而「未测定」的意思是**没量过**，不是**量过、钉住了**。换成表里自己的词「无覆盖」之后，
这句话可以从表第七栏机械复核。**「无覆盖」与「未测定」的定义抄自清单第〇节，本轮亲读核对过**
（第〇节原文里「无覆盖」有两种结果形态，本轮把两种都写进了限定域，未只写其中一种）。
3.7.1 表内 #12 单元格原本用的就是「闸门本身**无覆盖**」，**未改**。

### 五、收尾检查的实际输出

**1. 全量判据（与 CK-032 同一条命令，本轮重跑）**

```text
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
失败 0，通过    52，已跳过 0 — Dfo.Server.Networking.Tests.dll
失败 0，通过   524，已跳过 0 — Dfo.Server.Protocol.Tests.dll
失败 0，通过    60，已跳过 0 — Dfo.Pvf.Tests.dll
失败 0，通过     6，已跳过 0 — Dfo.Server.Host.Tests.dll
失败 0，通过   320，已跳过 0 — Dfo.Server.Persistence.Tests.dll
失败 0，通过   236，已跳过 2 — Dfo.Server.Infrastructure.Tests.dll
失败 0，通过  1039，已跳过 0 — Dfo.Server.Application.Tests.dll
EXIT=0
```

复算：`52 + 524 + 60 + 6 + 320 + 236 + 1039 = 2237` 通过，**2 跳过，0 失败** ✓
——与契约给的 2237／2／0 相同，与 QR-10-A 本轮之前的读数相同。未出现既知偶发。

**2. `git status --porcelain`**

```text
14 行 ` M` ＋ 7 行 `??` ＝ 21 行
（` M` 逐行与第一轮所记相同；`??` 逐行与第一轮所记相同）
```

本轮改的三个文件里，`AGENTS.md` 与 `ItemContainers.cs` 本来就在那 14 个 ` M` 里，
`behavior-change-list.md` 落在已是 `??` 的 `.rks-agent/tasks/server-agrees-what-an-avatar-is/`
目录内。**行数与分类都没变。**

**3. 字节锚**

| 文件 | 本轮散列 | 与所记相符？ |
|---|---|---|
| `SelectCharacterAcceptedTranscriptTests.cs` | `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69` | ✓ 未变 |
| `GroundPickupSlice.cs` | `cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d` | ✓ 未变 |
| `ItemContainers.cs` | `8e5bf924f482169ebec721171931024ed6a8543a7c3222dacbb80e3208e03bc1` | 由第一轮的 `be638097…` 变来，**只因本轮改了那段 `<remarks>`** |

**4. `ItemContainers.cs` 本轮仍然只改注释，两条当场可核的读数**

```text
$ awk "NR>=66 && NR<=118" Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs | grep -nvE "^\s*///"
2:    private const int JewelSocketLength = 30;
3:
51:    private const int NeverExpires = int.MaxValue;
52:
→ 改动落在的那一段里，非注释行只有这两行加两个空行，本轮一个字未动。

$ git diff 565d33fe -- Server/src/.../ItemContainers.cs | grep -E "^[+-]" | grep -vE "^(\+\+\+|---)" | grep -vE "^[+-]\s*///" | wc -l
44
→ 相对基线的非注释差异仍是 44 行，与 QR-10-A 亲数的 44 行相同，本轮一行未增未减。
```

### 六、本轮自查

**逐句问「这句在说一个个例，还是在说一类？」，说一类的三处及其处置：**

1. 「今天在 `Server/src` 里没有一处按名字取用这两个成员」——**说一类**。
   它的证伪方法与它写在一起（上面第二节那三条 grep 的读数），并明写「这条读数会随代码变化，
   不是永久事实」。**没有压成「不足以确定」**：三条检索的读数在手，该断的断了。
2. 「另外那 7 行记『未测定』，它们的闸门有没有判据钉住尚未测定」——**说一类**，
   而这一类正是「未测定」这个词在第〇节的定义本身，可从表第七栏机械复核。
   **这里必须说「尚未测定」，不能说得更强，也不能不说**——不说，读者就会把
   「唯一一个无覆盖」读成「另外 12 行都钉住了」，那正是 F-3 指出的错。
3. 「本仓对这个数只用『约 11.4 年』一个说法」——**说一类，且是一条规定，不是观测**。
   它写在 `AGENTS.md` 的须知里，形式是「今后怎么写」，并附了算式与「十一年半 = 11.5」的差值，
   读者可以照它核任何一处复述。

**由表得出的小结，本轮逐条当场复算过**：
13 ＝ 11 ＋ 2（未变，被排除的一族不进表）；5 ＋ 1 ＋ 7 ＝ 13（第七栏，未改数）；
2237 ＝ 52＋524＋60＋6＋320＋236＋1039（上面第五节）；
11.37 ＝ 358796511 ÷ 31556952；68.05 ＝ 2147483647 ÷ 31556952；
66.55 ＝ 2.1×10⁹ ÷ 31556952；5.99 ＝ 2147483647 ÷ 358796511；
0.13 年 ≈ 47 天 ＝ (11.5 − 11.37) × 365.2425。

**引文四处，本轮逐字核过出处**：
`AGENTS.md`「已知缺陷与覆盖缺口」那两句（截取处已注明，且经 `git diff 565d33fe` 证明写于本轮之前）；
`items-own-their-identity/spec-result.md`「Successor Guidance」第 4 条；
清单第〇节「无覆盖」「未测定」两条定义；清单 3.7 口径一改前原文（抄在本节第一段）。
**四处都是持久记载。**

**本轮没有做的**：没有改任何可执行代码或判据；没有重跑 QR-10-A 已核实的那些读数
（`WriteOneAsync` 汇流口、15 调用点／10 成员、同型 5 与可走到 5 的交集、分档 24、四处引文）；
没有重跑 3.7 末尾第 2、3、4 条所依赖的探针（那三处仍由记载自己明写为不足以确定）；
没有提交、没有建检查点、没有用任何 git 还原类命令。
