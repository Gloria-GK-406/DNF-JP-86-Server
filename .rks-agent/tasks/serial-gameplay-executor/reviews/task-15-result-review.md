---
kind: rks-review
target: tasks/TASK-15.md
scope: Task Unit result quick review（TASK-15 / T-004b6c，Contract Revision 2 ＋「主控方裁定（执行前）」＋「第 2 版修订」＋「主控方裁定（第二次停止之后）」；执行轮次：两段 Progress 之后的 Result 第 1 版，未提交）
verdict: approved
date: 2026-10-22
rounds: "Round 1 changes_required（B1、B2）→ Round 2 approved（见文末 ## Round 2）"
---

# TASK-15 结果快速审查

## 审查对象与依据

- 目标：`tasks/TASK-15.md` 的 Contract（Revision 2）、Detailed Plan、三次主控方裁定、两段 Progress 与 Result。
- 改动身份：基准 `bfbcf012` ＋ 未提交改动。`git diff HEAD`：`Server/src` 33 个已跟踪文件、`Server/tests` 3 个、`spec.md` 1 行（附录 B）；未跟踪新文件 `IContentWarmup.cs`、`ContentWarmup.cs`、`CharacterSkillRuleWarmup.cs`、`ContentWarmupTests.cs`，以及 `verification/logs/task-15-*`（13 个）、`verification/measurement/task-15-coldstart.md`、`verification/probe-tools/firstload/`。
- 依据：`spec.md` RQ-091、附录 B「启动预热」行；`plan.md` 共享决定「预热」；`TASK-14.md` 的裁定与定向复核；`TASK-03.md` 的处置与 `verification/measurement/README.md` 的口径；AGENTS.md（后缀约定、规则服务／内容源的注入面、「真实进程用例满载假红」规则）。
- 采用的准则：reviewing-work 的 Task Unit 快速审查 ＋ Code（限本地明显缺陷）＋ Deliverable（附录 B 与 Result、证据的一致性）。
- 本审查自己做的核对：
  - 逐行读了 `IContentWarmup.cs`、`ContentWarmup.cs`、`CharacterSkillRuleWarmup.cs`、`GameServerStartupService` 的改动、两个模块的注册，以及 30 个参与者的全部差异；
  - 读了 `ContentWarmupTests.cs` 全文、S1 与两个宿主的差异；
  - 抽查了 `JoustRuleService`、`DropRuleService`、`PvfDropItemPoolSource`、`CharacterSkillRuleService`、`PvfSkillPointDefinitionSource`、`PvfDungeonRoomDropSource`、`PvfDungeonRoomActorSource`、`PvfItemPlacementDefinitionSource`、`PvfAchievementDefinitionSource`、`PvfTown/QuestDefinitionSource`、`PvfEquipmentSlotDefinitionSource`、`PvfNpcShopDefinitionSource` 等的缓存形状；
  - 核对了 30 个 `AddContentWarmup<TService>` 的生产注册次数与生命周期；
  - 读了 `task-15-firstload-r2.txt`、`task-15-slow.txt`、`task-15-isolated.txt`、`task-15-boundary.txt`、`task-15-mutations.txt` 开头、`task-15-coldstart.md`；
  - 定向构建 Host.Tests 并跑了一次变异实验（见 B1），以及还原后的 S1 ＋ `ContentWarmupTests`（7／7 通过，S1 日志 `Content warmup finished: 31 items in 1868 ms`）。没有跑全量。

## 阻断项

### B1. W1 先自己把全部参与者跑一遍，再核对字段——它证明不了「启动时已加载」

**现象**：`ContentWarmupTests.AssertWarmedAsync` 的顺序是

1. 名字集合与 `Participants` 相等；
2. 对每个 `IContentWarmup` **调用 `WarmAsync`** 并断言返回 > 0；
3. 然后才逐个持有者核对 `Lazy` 已创建、`??=` 字段非空、按键覆盖全部键。

第 2 步本身就会加载第 3 步要核对的全部字段。所以对 31 个参与者而言，第 3 步核对的是「`WarmAsync` 会加载这些字段」，不是「宿主就绪时这些字段已经加载」。

**实验**（本审查做的临时变异，已还原）：删掉 `GameServerStartupService.StartingAsync` 里唯一那句 `await ContentWarmup.RunAsync(...)`，构建 Host.Tests，单跑 S1：

- S1（含 W1）**通过**；
- 该次运行的输出中 `Content warmup` 出现 0 次（预热确实没有在启动时执行）。

在 `Server/tests` 中检索 `ContentWarmup`／`Content warmup`，只命中 `ContentWarmupTests.cs`、S1 与两个关开关的宿主。也就是说，**「启动时调用预热」这件事今天没有任何用例守着**：删掉这一句、把它挪到 `StartAsync` 之后、或者 S1 里开关被读成 false，全量都是绿的。V9、V10 能变红，是因为它们改坏的是参与者本身（注册缺一项、技能只热一个职业），W1 重跑参与者时照样暴露；它们不检验启动顺序。

**后果**：
- Contract 的 Outcome「执行器上不再首次加载」的证据里，W1 是唯一的运行期判据。§3.1 写的「W1 证明清单内各项在宿主就绪时已加载」、附录 B 证据栏写的「持有者的 `Lazy`／`??=` 字段都已加载」，现状下都不成立。
- 本单元的全部价值在于加载发生在开始监听之前、执行器之外。启动调用一旦被重构掉，测试不报，问题要到 M-1 测量或实机上才以「执行器上数百毫秒的停顿」出现——这正是 Result review 第 1 条担心的那类漏网，只是这次漏的是调用点而不是清单项。

**最小修正**：
- 在 `AssertWarmedAsync` 中，先做字段核对（第 3 步），再调用 `WarmAsync`（第 2 步）。字段核对放在前面，才是在检查启动后的状态；`WarmAsync` 留在后面，继续满足第 2 版修订第 4 条「每一项都成功，且条目数大于 0」。
- 或者另加一条：从 S1 宿主的日志捕获里断言恰有一条 `Content warmup finished: 31 items`、0 条 `Content warmup failed`。
- 两种做法任选其一。之后补一项变异：删掉 `StartingAsync` 中的预热调用，W1 应变红。写进 `task-15-mutations.txt`，并相应更正 §3.1 与附录 B 证据栏的措辞（如有必要）。

### B2. 附录 B「留给 M-1」漏了执行器上仍会首次加载的整索引，现有措辞还会让人以为它们不在执行器上

**现象**：
- 附录 B 把 35 个豁免类型概括为「其中 32 个是『探针没有在执行器上触达』」。「留给 M-1」一栏只有三项：首次选角约 100 ms、C／D 类耗时、RQ-091 判定。**这 32 个不在里面**。
- Result 的 Remaining 第 4 条写了「列给 M-1」，附录 B 却没有照此落下。

**实际情况**（抽查）：这 32 个全都是 `??=` 整索引，没有一个是 `Lazy<>`（逐个统计：`Lazy<` 0 处，`??=` 1～5 处）。其中多个由明确的玩法 CMD 使用，例如：
- `PvfNpcShopDefinitionSource`（`NpcShopRuleService`）首次调用读 `npc.lst`、`itemshop.lst`、`stackable.lst`、`equipment.lst`；
- `PvfItemSaleDefinitionSource`、`PvfMailItemDefinitionSource`、`PvfEquipmentRepairDefinitionSource` 读 `equipment.lst`（约 11.8 万行）或 `stackable.lst`；
- `PvfEquipmentReinforcementDefinitionSource`、`PvfItemUsageRuleSource`、`PvfTeleportConsumableDefinitionSource` 等，路径同理。

它们和本单元已经预热的 `item-placement-index` 是同一种东西。区别只在于合成负载没有走到它们。另外还有一类 W2 认不出、也不在豁免表里的 `is null` 写法整索引，例如 `PvfEquipmentSlotDefinitionSource._paths`（`equipment.lst`，经 `ItemPlacementRuleService`）。

**后果**：
- M-1 的测量用的是同一套合成负载，它同样走不到这些路径。按附录 B 现在的写法，M-1 不会知道还有一批整索引要在执行器上首次加载，可能凭 (f) 的结果就判定 RQ-091 已经达标。
- 但 RQ-091 原文是「执行器上会触达的全部」，而 (c)～(e) 还有一条 max ≤ 250 ms 的单条执行时长要求。
- 「探针没有在执行器上触达」说的是这次负载的覆盖范围，不是这些类型在生产中的性质。豁免理由的原文（`NotReached` 常量）其实写清楚了这一点，只是附录 B 的概括把这层意思丢掉了。

**最小修正**（纯文字）：在附录 B「留给 M-1」一栏补一项，写明三件事：
- 32 个 `??=` 豁免持有者，以及 W2 认不出的 `is null` 写法整索引，在合成负载之外的玩法 CMD 上**仍然会在执行器上首次加载**（举两三个例子即可）；
- M-1 的负载同样覆盖不到它们；
- 它们是按第 2 版修订第 2 条的范围裁定留下的，RQ-091「执行器上会触达的全部」对它们是否成立，由 M-1 或用户判定。

Result 的 Remaining 第 4 条照此补上「M-1 负载也覆盖不到」这一句。

## 非阻断项与残余风险

1. **W2 的识别方法是近似**，可靠程度如下。
   - **可靠的部分**：
     - 反射识别 `Lazy<>` 与 `ConcurrentDictionary<,Lazy<>>` 实例字段，可靠；
     - `??=` 用源码正则识别，只认「同文件声明的类型有同名实例字段」，误报低；
     - 反例用例与 V11 都证明判据会拒绝。
   - **漏判的形状**（Result 只承认了第一种）：
     - `if (x is null) { …; x = … }`（例：`PvfEquipmentSlotDefinitionSource._paths`、`PvfDungeonDefinitionSource` 自身）；
     - 手写字典缓存（例：`PvfDungeonRoomDropSource.paths[folder] = …`）；
     - `if (definition is {} loaded)`（例：`JoustRuleService`）；
     - 静态字段（当前没有静态 `Lazy`，已检索）。
   - **影响**：附录 B 的约束「新增 `Lazy` 或 `??=` 首次索引的持有者……由 W2 守着」只对前两种形状成立。建议在附录 B 的约束后加半句，写明这一边界。不改代码也可以。
2. **打包进程用例的 15 s 预算（主控方裁定：按 AGENTS.md「满载假红、隔离复跑」接受，并入 TASK-16）**：
   - **同意这个裁定**，证据成立：
     - 隔离 5 轮全部通过，每次约 3 s；
     - 复跑的全量里通过；
     - CI 没有 PVF，不走预热，不受影响。
   - **但要记明一点**：这次不全是机器噪声。预热在满载下会放大到 8～14 s（同一次全量里 S1 的预热用了 14234 ms，其中 drop-rules 10083 ms），而打包进程保持开启。所以只要本机带 PVF 跑全量，这一类假红出现的概率是本单元实打实抬高的。
   - **建议**：TASK-16 的重型宿主检查里，单列「带 PVF 的打包进程用例清单及各自预算」，不要只按 AGENTS.md 的通则处理。
3. **首次选角剩下的约 100 ms**：
   - 归因为「JIT 与 SQLite 冷页」是推断，Result 与测量记录都已标明，并交给了 M-1。
   - 三条依据成立：P2 没有降下来；r2 中选角没有 A／B 类首次加载；之后的选角约 3 ms。
   - 补充一点：D 类在首次选角里同样有读档，每次选角 4 次 `ReadRules`、3 次周签到、1 次超额技能契约。这部分代码路径的首次 JIT 也算在这约 100 ms 里。M-1 拆分时，不要把它当作纯数据库冷页。
4. **`ContentWarmupRegistration` 的占位分支**：
   - 生产中不会误触发。已核对：30 个 `TService` 在生产模块中各只注册一次，全部是单例，Host、GmWeb、GameServer 中没有对它们的替换或装饰。即使以后有人加了装饰器，W1 的名字集合比对也会在生产组合下变红。
   - 小建议：占位被解析时只留下一条 Information 汇总行，可考虑给它记一条 Warning，这样生产日志里也能一眼看到。不阻断。
5. **汇总行的计数口径**：`Content warmup finished: {Items} items` 中的 `items` 把失败项也算在内（F1 断言的就是「3 items」，其中 1 项失败）。check 2 拿它统计宿主启动次数，不受影响；若要拿它当「成功项数」用，需要另行说明。
6. **契约放置与命名**：
   - `IContentWarmup` 放在 `Abstractions/Content`，不带 `DomainService` 等后缀，与第 2 版修订的命名一致。它不属于 AGENTS.md 后缀表的任何一类，Result 已说明没有改那张表。可以接受。
   - 启动组件经这个契约直接触达内容源的入口，有裁定第 5 条与 `StartingAsync` 已有的先例，没有改变运行期的注入面。
   - `CharacterSkillRuleWarmup` 不是命令处理器，不受构造器注入判据约束。职业集合由它作为 facts 交给规则服务，符合「规则服务显式接收职业」。

## 已核对、无问题的部分

- **31 个参与者的入口与探针抓到的内容**：抽查了 8 个。
  - **drop-rules**：怪物掉率表、金币表，以及 `Enum.GetValues<DropItemFamily>()` 全部族的物品池。物品池是 `ConcurrentDictionary<,Lazy<>>`，默认模式。
  - **altar-dungeons**：11006、11007 两个副本，连带地图的按副本索引。
  - **5 个路径索引**：
    - room-actor 读 `map`、`aicharacter`、`passiveobject`，room-object 读 `map`、`dungeon`、`equipment`、`passiveobject`，都与各自内容源实际调用 `Paths(...)`、`ReadSection(...)` 时的文件夹一致；
    - dungeon-index、dungeon-map-index 在锁内调用 `Paths()`；
    - item-placement-index 的 `Index()` 只是把原来的两行 `??=` 原样抽出来，`GetAsync` 照旧在锁内调用它。
  - **character-skill-rules**：职业集合取 `SupportedCareers` 的职业号并去重，经 `GetAll(job)` 同时填满 `CharacterSkillRuleService.jobs` 与 `PvfSkillPointDefinitionSource.catalogs`。W1 的键超集断言与 V10 一起守着这一点。
  - **premium-service-rules**：10 个 `Lazy` 全部触达。
  - **experience-rules**：经验参数、评级、频道经验三个内容源，加上进度策略。
- **失败语义**：
  - **成就目录**：`new Lazy(factory)` 的默认模式是 `ExecutionAndPublication`，会缓存异常，之后的请求拿到同一个异常，与今天相同。
  - **城镇、任务目录**：`??=` 不缓存失败，请求时重试，与今天相同。
  - **Joust**：失败返回 `(null, error)`，不缓存，请求时重试，与今天相同。
  - 在 Application 与 Infrastructure 中检索 `PublicationOnly`，命中为 0。
  - 取消：`catch (OperationCanceledException) when (token.IsCancellationRequested)` 照常抛出，F1 有用例守着。
- **开关**：
  - 缺省为 true；显式 `false`（大小写不敏感）才关闭；非法值抛异常，宿主启动失败，属于显式失败，可以接受。
  - 仓库里所有 json、props 配置都没有这个键。
  - 必须开启的几类确实开启：
    - `CharacterizationHost` 没有设这个键，它承载全部表征测试与停机 S2；
    - S1 显式设为 true；
    - 打包进程走生产 appsettings。
  - 只有 `GmTestHost` 与 `ComponentLifecycleTests` 设为 false。
  - 环境变量的形式是 `ContentWarmup__Enabled`，没有发现命名冲突。
- **附录 B**：只改了这一行，原文「在开始监听前加载完毕……实施时逐个列出」逐字保留，后面追加清单、机制、开关、证据、不预热项、约束与交 M-1。「两个启动作业之后调用」与代码一致。缺漏见 B2。
- **数字与日志一致**：
  - 全量总数 8136 的加法；
  - 耗时 2690.4 → 2723.7 s，＋1.2%；
  - Host.Tests 隔离 182 条通过，1.75 min；
  - RSS 13454768 KiB 对 TASK-14 同一脚本的 11440064～15931440 KiB，单位都是 KiB，口径写明；
  - 冷启动对照表：(f) 133.6／137.6 ms、首次选角 101.7／104.3 ms，与 Result 一致；
  - 预算 +1838／+1359 ms、RSS +9.6%／+15.6%，都在裁定第 1 条之内；
  - r2：5193 ＝ 5193，清单内项为 0。

## 变异实验的还原证明

- 文件：`Server/src/Dfo.Server.GameServer/GameServerStartupService.cs`。
- 变异前 sha1 `378fd52be379d52fd0209210c471dd400f709ef1`，从 scratchpad 备份还原后 sha1 相同，`cmp` 相等。
- `git status --short` 的输出 sha1 前后都是 `8abe288b9898dafa845052c55aec30ef04aed82e`（57 行）。
- 还原后重新构建 Host.Tests（0 警告、0 错误），`Dfo.Server.GameServer.dll` 大小回到 22528 字节（变异版是 22016 字节）。复跑 S1 ＋ `ContentWarmupTests` 共 7 条，全部通过，S1 日志中有 `Content warmup finished: 31 items`。
- 没有 commit，没有 stash，只新增了本审查文件。

## 处置建议

- B1、B2 改完之后做定向复核：
  - 单跑 S1 ＋ `ContentWarmupTests`；
  - 做新变异「删掉启动调用 → W1 变红」，确认后逐字节还原；
  - 核对附录 B「留给 M-1」的补句。
- 不需要重跑全量：两项修改都不改生产代码。B1 只改测试里的断言顺序，或加一条日志断言。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

### 范围与依据

- **对象**：`tasks/TASK-15.md` 的「Progress（第 3 轮）」、主控方裁定（第 3 轮停止之后，放宽就绪 RSS 预算）、第二次 Progress（耗时 +24.4%）以及「Result（第 3 轮）」。用户的新指示也在依据之内：测试端的耗时与 RSS 两项停止条件撤销，只作记录。
- **改动身份**：基准 `bfbcf012` ＋ 未提交改动。
  - `git diff HEAD`：`Server/src` 73 个已跟踪文件（＋1157／−142 行）、`Server/tests` 3 个、`spec.md` 1 行；
  - 新文件：`IContentWarmup.cs`、`ContentWarmup.cs`、`CharacterSkillRuleWarmup.cs`、`ContentWarmupTests.cs`；
  - `git status --short` 共 106 行。
- **只看本轮**：
  - B1 的修正；
  - B2 的处置：可达性判定、40 个内容源的写法、豁免；
  - 附录 B 的重写；
  - 第 3 轮的数字，以及测试端代价的记录。
- Round 1 中已核对、本轮没有变动的部分，沿用 Round 1 的结论。

### Round 1 阻断项的复核

**B1：已解决，本审查独立复现。**
- `AssertWarmedAsync(services, hostLog)` 现在的顺序是：
  1. 名字集合相等；
  2. 宿主日志中**恰好一条** `Content warmup finished: {Participants.Count} items, 0 failed,`，并且没有 failed 或 skipped 行；
  3. 在调用任何 `WarmAsync` **之前**核对持有者字段（`AssertHoldersLoaded`）；
  4. 最后每项再调用一次，返回值大于 0。
- S1 把 `host.Logs.Messages` 传进 W1。`GmTestHost` 的 `CreateLogger` 把所有类别都写进这个队列，所以 `GameServerStartupService` 的汇总行也会进去。
- **本审查的 V15 复现**：删掉 `StartingAsync` 中的 `ContentWarmup.RunAsync(...)`，构建 Host.Tests，单跑 S1。结果**变红**，失败在 `ContentWarmupTests.cs:232` 的 `Assert.Single`（日志断言）。
  - 这条日志断言也能抓住「开关被读成 false」：那种情况下宿主记的是 skipped 行，没有 finished 行。
  - 还原后，sha1 `378fd52b…09ef1` 前后相同，`cmp` 相等；`git status --short` 的 sha1 前后都是 `de85ebe7…`。
  - 重建后，S1 与 `ContentWarmupTests` 共 8 条全部通过，S1 日志为 `Content warmup finished: 71 items, 0 failed, in 2644 ms`。
- 执行者的 `task-15-mutations.txt` 第 3 轮段也记了 V15 变红，快照 sha1 `94d37c00…` 前后相同。

**B2：已解决，而且主控方的处置比审查建议更严**：不是只补文字，而是把可达的整索引都纳入了预热。

- **可达性判定表（抽查 12 项纳入，全部豁免都核对了）**：
  - **纳入项**：抽查了 12 项，逐个用 grep 追调用链：内容源接口 → Application 的规则服务或领域服务 → Protocol 的切片或命令。都成立，例如：
    - `IAvatarCompoundDefinitionSource` → `AvatarCompoundRuleService` → `AvatarCompoundDomainService`；
    - `IRecipeContentSource` → `RecipeCraftDomainService` → `CompoundItemSlice`；
    - `ICargoItemDefinitionSource` → `CargoTransferDomainService` → `MoveItemSpaceSlice`；
    - `IInventorySortKeySource` → `InventorySortDomainService` → `SortItemCommand`、`SortLockCommand`；
    - `ICycleDungeonDefinitionSource` → `CycleDungeonRuleService` → `CycleDungeonEntryDomainService`；
    - `IQuestParameterSource` → `QuestContentRuleService` → `QuestMapClearSubscriber` 等；
    - `ITutorialRewardDefinitionSource` → `TutorialRewardDomainService`；
    - 另外 5 项：`IEquipmentForgingContentSource`、`IEquipmentOutworldContentSource`、`IInventoryExpansionDefinitionSource`、`ILuckyStarRentalContentSource`、`ICreatureEquipmentLifetimeSource`，它们的消费者分别是 `EquipmentUpgradeRuleService`／`ItemEquipmentRuleService`、`CeraShopRuleService`／`InventoryExpansionRuleService`、`LuckyStarRules`、`CharacterEntryPreparationRuleService`／`CreatureRuleService`。
  - **有界键的那两项**：
    - `drop-rate-tables` 预热 3 张掉率表加 3 张金币表；
    - `clear-reward-rates` 预热 ClearReward 一张表；
    - 本审查在 Application 中检索 `Load(DropRateTables.*)` 的全部调用方，所用的键恰好就是这几个。
  - **豁免**：W2 表里 4 项，判定表另有 1 项，都成立。
    - `PvfItemTooltipSource`：唯一使用方是 `GmItemEndpoints` 的 tooltip 路由，该路由带 `OutsideGameplay("PVF 内容源与缓存：物品说明")`；
    - `PvfArchiveIdentity`：唯一使用方是 `/api/gm/v1/server-info`，同样是 `OutsideGameplay`，而且它是档案哈希，不是内容索引；
    - `PvfArchiveProvider`：档案本身；
    - `InventoryEffectPreflightDomainService+Standing`：单次调用里构造的对象；
    - `PvfEntryLifecycleContentSource.slotMappings`：在构造器里建出的规则对象，不读档案。
  - **漏项检索**：
    - 在 `Infrastructure/Content` 中找「读 `.lst` 却没有实现契约」的文件，剩下的都已由已有启动项覆盖（额外掉落、基础属性、怪物声明掉落、领主塔、自动售货机、次元副本、独立掉落），或由参与者间接预热（技能点、礼包、比武内容），或属于 D 类（`PvfEntryLifecycleContentSource`），或已豁免（tooltip），或者是在加载过程中临时构造的读取器（`PvfDropItemReader`）；
    - `is null` 写法的剩余命中（`QuestKillObjectiveParser`、`PvfCharacterStatDefinitionSource`、`PvfDungeonExperienceProgressionPolicySource`）都是局部变量，不是缓存。
  - 没有发现遗漏的可达整索引。
- **写法是否保持请求路径行为不变**：40 个文件的差异逐个过了一遍（`git diff -U4` 的 2103 行）。
  - **全部是机械搬移**：原来的 `F ??= 表达式;`，或 `if (_paths is null) {…}` 整块，原样移进一个私有方法，原调用处在同一位置、同一个锁里改为调用它。表达式、键与值的形状、`"stackable/"` 等前缀一处没变。
  - **并发**：请求路径仍在原来那把锁内调用；预热也在同一把锁内调用。而且预热发生在 `StartingAsync`，早于任何监听与事件循环，没有并发的首次调用。
  - **异常**：`??=` 失败时字段保持 null，下次请求照旧重试；按键的 `ConcurrentDictionary.GetOrAdd(key, Read)` 不缓存失败。两者都与改动前相同。
    - 某个参与者的多个子索引里如果中途有一个失败，前面已经加载的子索引会留下。这些子索引本来就会被请求按同样的表达式加载，所以请求方看到的结果不变。
  - **返回值**：请求方法的返回路径没有改动；`[MemberNotNull]` 只影响编译期。
  - 3 个类本来就有私有的 `Name(...)` 方法，这里用显式接口成员实现 `Name`，编译与调用都正确。
  - `PvfExperienceItemDefinitionSource` 的「没有档案就返回 null」守卫仍在原位。预热只在有档案时运行，所以这个守卫的语义不受影响。

### 附录 B 的重写

- `spec.md` 的差异只有一行（＋1／−1），就是「启动预热」这一行。行首原文「在开始监听前加载完毕……实施时逐个列出」逐字保留。
- **内容**：机制、开关、71 个参与者（名单只引用 `ContentWarmupTests.Participants`，不重抄）、证据（含 V15）、不预热的三类、W2 的识别边界、约束、TASK-12 的关闭依据、留给 M-1。
- **交 M-1 的清单完整**：
  - C 类：单键首次加载与 `searchable`；
  - D 类：周签到被单列为性能关注点；
  - 约 100 ms 的归因，附 JIT 的补充说明；
  - 「合成负载覆盖不到的 CMD 走到 C／D 类时仍在执行器上首次加载，M-1 的负载同样覆盖不到」这一句，Round 1 的 B2 要的正是它；
  - RQ-091 的达标判定。
- 整索引类现在都已经预热，不再需要交给 M-1。

### 第 3 轮的数字与测试端代价的记录

逐项核对过，与日志一致：

| 项 | 数字 | 出处 |
|---|---|---|
| 第一样本 | 2682.7 → 3338.4 s，＋24.4% | `task-15-slow.txt` |
| 第二样本 | 2690.8 → 3399.7 s，＋26.3%；对第 2 轮 ＋24.8% | `task-15-slow-r3b.txt` |
| 第二样本中的预热 | 1 条 `Content warmup finished: 71 items, 0 failed, in 54197 ms`，`Content warmup failed` 0 条 | `task-15-build-test-r3b.txt` |
| Host.Tests 隔离 | 183 条全部通过，1.96 min；RSS 峰值 14247072 KiB（同一脚本，单位 KiB） | `task-15-isolated.txt` |
| 冷启动（第 3 轮 P1） | 就绪 6445／3936 ms；就绪 RSS 1231984／1223968 KiB（＋76.5%／＋91.2%） | `task-15-coldstart.md` 第 3 轮一节 |
| 13 份重复字典 | 逐项相加 503025 KiB，本审查复算无误 | 同上 |
| 变异 | V9～V15 | `task-15-mutations.txt` 第 3 轮段 |
| 测试改动核对 | 已按第 3 轮的 S1 调用（带 `host.Logs.Messages`）重做 | `task-15-diff-check.txt` |

**测试端代价的记录是如实的**：
- 两个样本都写了；
- 写明了 S1 在满载下的预热用时 50～54 s（单测 60 s），每个开着预热的宿主约 1.2 GB；
- swap 的增长写了；
- 推断都标了「推断，未测」；
- 还附上了临时关闭表征宿主预热那次的 ＋8.2% 作对照，该改动已按用户指示撤回；
- 「生产稳态也会到同一量级」也照实标为推断、未测，并说明了为什么现有数据不足以支持它。

### 非阻断项（不影响本轮通过）

1. **Result（第 3 轮）中参与者的构成写错了**。原文是「Infrastructure 62 个内容源 … Application 9 个」。实际情况：
   - `InfrastructureModule` 注册了 **63** 个参与者，分布在 62 个文件里，因为成就与称号簿两个类在同一个文件；
   - Application 是 **8** 个：7 个经 `AddContentWarmup`，外加 `CharacterSkillRuleWarmup`；Result 把 `ApplicationModule` 也算成了一个参与者。
   - 总数 71 是对的，只是两项拆错了。建议改成「63 ＋ 8」。
2. **附录 B「留给 M-1」中的首次选角**写的是第 2 轮的 101.7／104.3 ms。第 3 轮 P1 的值是 101.1／104.2 ms，结论不变，可以顺手统一。
3. **冷启动表里 t＝60 s 的 RSS 与全程峰值，第 3 轮比第 2 轮高出约 0.7～0.8 GB**（1791488／1749584 KiB 对 904784／1051616 KiB）。这比就绪时的增量还大。
   - Result 的 Outcome 只写了就绪 RSS。数字已经在测量表里，没有隐瞒；
   - 建议在 Remaining 第 1 条（13 份重复字典，交主控方立项）旁注一句「t＝60 s 与峰值 RSS 也高出约 0.8 GB」，方便立项时评估。
4. **Round 1 的非阻断项第 2 条已由 Remaining 第 2 条接住**，并注明交 TASK-16 单列（带 PVF 的打包进程用例与 15 s 预算）。第 3 轮的两个样本里那条 Launcher 用例都通过了，但满载下 S1 的预热达到 50～54 s，风险仍在，记录如实。

### 本轮变异的还原证明

- 文件：`Server/src/Dfo.Server.GameServer/GameServerStartupService.cs`。
- 备份在 scratchpad `rev15/GSS-r2.bak`。
- 还原后 sha1 `378fd52be379d52fd0209210c471dd400f709ef1` 前后相同，`cmp` 相等。
- `git status --short` 的 sha1 前后都是 `de85ebe710694113de17eaac22f11fb9fdaa108e`（106 行）。
- 还原后重建 Host.Tests，0 警告、0 错误；8 条定向用例全部通过。
- 没有 commit，没有 stash，没有跑全量，也没有同时跑多个测试项目。
