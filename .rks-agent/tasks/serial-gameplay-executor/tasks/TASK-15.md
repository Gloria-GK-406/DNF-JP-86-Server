# TASK-15 — T-004b6c：启动预热

基准提交 `bfbcf012`（TASK-14 之后）。本单元是 TASK-14 待裁定第 1 条拆出的批 C，编号依据见 `TASK-14.md`「主控方裁定（执行前）」第 1 条；原 b7 对账收口顺延为 TASK-16。

本单元的输入（只引用，不重抄）：
- `TASK-14.md` §5（原则、清单、探针）、§6 W1 行、Required check 6 的 V9 与 check 11、§12 第 4 步。本文件以它们为起点，**在两处改写**：
  - 「按键、无界的缓存」的口径，按 TASK-14 主控方裁定第 10 条「先测量再定」，测量方案见 §5；
  - W1 不再新开宿主，见 §7（TASK-14 定向复核之后的裁定：Host.Tests 内存压力，不再增加重型宿主用例）。
- `TASK-12.md` Result 第 5 条：事件作业可能早于预热运行，在执行器上首次加载成就、称号目录。
- `TASK-03.md` 主控方处置与 `verification/measurement/`：
  - 工具与命令行见 `README.md`；判定序列为「计划发出 → 第一条回包」；
  - 基线（构成 B）：Host 就绪 1397 ms（`baseline-clean-1.md`）；冷进程首次 `PrepareSelectedCharacterStateCommand` 502.8／520.2 ms；首次进本 `EnterDungeonCommand` 1103.9 ms（B-2，t＝15 s）；(f) 最长单次停顿 575.9／1118.5 ms。
- `plan.md` 共享决定「预热」；`spec.md` RQ-091（(f) ≤ 250 ms；执行器上会触达的全部 `Lazy` 内容源进入启动预热清单并列入附录 B）、附录 B「启动预热的 `Lazy` 内容源」行。

## Task Unit Contract

- Revision: 2（第 2 版：改用统一预热契约，见文末「第 2 版修订」）
- Outcome and references：
  - **预热清单**（RQ-091 的预热部分，附录 B「启动预热」行）：用静态检索与首次加载探针两种方法确定（§2、§4）。清单中的内容在 `GameServerStartupService.StartingAsync` 里、执行器外、开始监听之前加载完毕（§3）。
  - **「执行器上不再首次加载」的证据**：
    - 首次加载探针两轮。基准上的原始清单 → 预热后清单内项为 0，清单外项逐条处置（§4）；
    - 运行期判据 W1、结构判据 W2，以及失败语义判据 F1（§7）。
  - **无界按键缓存**（按副本、按地图、按物品等）：按 §5 测出两组数，一是它们在冷启动停顿中的占比，二是全部预热带来的启动时长与内存增量；按「待裁定」第 1 条的口径处置。
  - **冷启动测量**（RQ-090 (f)，构成 B，判定序列）：与 TASK-03 基线对照，判断 (f) 中「内容加载」那部分是否已经消除（§6）。RQ-091 的达标判定归 M-1。
  - TASK-12 Result 第 5 条在本单元关闭（§3.1 次序 ＋ W1）。
  - 覆盖：RQ-091 的预热部分；附录 B「启动预热」行的清单；AC-006 中该行的对账。
- Owned boundary：
  - 生产代码：
    - `Dfo.Server.GameServer/ContentWarmup.cs`（新增，internal）：预热清单与逐项执行（§3）。
    - `Dfo.Server.GameServer/GameServerStartupService.cs`：在 `if (archive is not null)` 分支内调用 `ContentWarmup`，已有各项与两个启动作业都不改。
    - `Dfo.Server.GameServer/Dfo.Server.GameServer.csproj`：加 `InternalsVisibleTo Dfo.Server.Host.Tests`，只供 F1 与 W2 使用。
    - §2.3 表中各拥有者，以及探针第 1 轮补入的有界项拥有者（§2.4）：在 Abstractions 接口与实现上**只增加**一个预热入口，形状见 §3.3。不改其他成员，也不改加载逻辑本身（读什么、怎么缓存都不动）。
  - **不改**：
    - 执行器、队列、作业、`GameplayExecutorLifetime`；
    - `CommandBus` 与管线；
    - 已有的预热各项与两个启动作业；
    - `PvfArchiveProvider` 的生产代码（探针改动临时，不提交）；
    - `Tool/GameplayLoad` 源码；
    - 各内容源的加载与缓存实现。
  - 测试：
    - 新增 `Server/tests/Dfo.Server.Host.Tests/ContentWarmupTests.cs`（W1 断言辅助、W2、F1，以及 W2 的反例）；
    - `GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs`（S1）末尾加一行，调用 W1 辅助，原断言一条不动（§7.1）；
    - 实现了被加成员接口的测试替身：只补这一个成员，返回 0、不做任何事（§8）。
  - 文档与证据：
    - `spec.md` 附录 B「启动预热」行：按 TASK-09 的先例，只在单元格末尾追加清单（§9）；
    - `verification/logs/task-15-*.txt`；
    - `verification/measurement/task-15-coldstart.md`（§6）；
    - `verification/probe-tools/firstload/`（新增）：首次加载探针的源码模板、打补丁／还原脚本、汇总脚本（§4）。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘到 `logs/task-15-build-test.txt`。
     - 总数：TASK-14 为 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 969 ＋ 173 ＋ 4502 ＋ 176 ＝ 8130（HttpApi、Pvf、Protocol、Persistence、Infrastructure、Networking、Application、Host）。本单元 ＝ 8130 ＋ N － D，逐项目对照并写出加法。预计只有 Host 增加。
     - 失败只能是那 2 条 `DimensionDungeonTests`；跳过 20 条；表征测试 8 通过、2 跳过；没有挂起转储。
     - 日志信号与 TASK-14 Checks 2 相同的那几项，另加本单元的 `Content warmup failed` 计数。非零的逐条说明来源。
     - **耗时比对**：与 `logs/task-14-build-test.txt` 逐条比，多出 ≥ 9 秒的逐条判定。Host 与 Networking 单列，因为预热让每次「有档案」的宿主启动都变长（§10 第 2 条）。
       - 用 Host 日志中的预热汇总行，统计全量里运行了预热分支的宿主启动次数，以及每次的预热耗时；日志里取不到的，按代码检索给出上界，并写明「日志不足以确定」。
     - **测试进程内存**：Host.Tests 单独全量跑一次，每 5 秒采样 testhost 的 RSS，与 TASK-14 定向复核的 10.9～15.2 GB 对照。
     - 出现 > 30 秒的停顿时，按 TASK-14 Remaining 第 3 条的顺序排查：先本单元的路径（预热耗时与内存），再换页，再线程栈。不得直接归因于负载（TASK-14 主控方裁定）。
  3. **隔离复跑**：Host.Tests 与 Networking.Tests（加 `--blame`）各一次，落盘到 `logs/task-15-isolated.txt`。这两个项目里有带真实档案的宿主（表征宿主、打包 exe），预热会作用在它们身上。超时形态的失败先按 AGENTS.md「满载假红」隔离复跑。
  4. **新增测试 ×10**：W1（随 S1）、W2 及其反例、F1，隔离连续跑 10 次，全量负载下再跑 1 次，全部通过，落盘到 `logs/task-15-new-x10.txt`。
  5. **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本不改。预热只把加载提前，线上字节不应变化；一旦变化，停下回报，不改金样本，也不放宽比对。
  6. **变异核对**：每项用 scratchpad 备份，逐字节还原后 `cmp`，全部做完后以 `--no-incremental` 重新构建，落盘到 `logs/task-15-mutations.txt`。
     - V9：从预热清单删掉成就目录一项，W1 变红（即 TASK-14 的 V9）；
     - V10：技能规则只预热第一个职业，W1 变红（按键覆盖不全）；
     - V11：给 §2.3 表外的某个内容源临时加一个 `Lazy<int>` 字段，W2 变红；
     - V12：去掉逐项的异常处理，让失败直接抛出，F1 变红。若「待裁定」第 3 条改选「失败即启动失败」，V12 改为「加上吞异常」，F1 同样变红。
  7. **测试改动核对**：新增测试逐条列名，写出 N 的加法。§8 中补了成员的替身文件逐个列出，并用 TASK-07 的差异核对脚本（scratchpad `t07/diffcheck7.py <基准>`）证明：方法集合不变，断言行的多重集合不变，差异只有新增的那一个成员。S1 的改动写成「原断言 → 原断言 ＋ W1 辅助调用」。
  8. **生产改动的边界**（`logs/task-15-boundary.txt`）：
     - `git diff --stat -- Server/src` 只含 Owned boundary 所列文件；
     - 接口上新增的成员只有 §3.3 所述的预热入口，逐个列出；
     - `IGameplayQueue`、`SuppressFlow|RunDetached` 的命中与基准逐行相同；
     - `ContentWarmup` 不入队、不取门、不开事务：`grep "IGameplayQueue\|DomainTransaction\|Gate" ContentWarmup.cs` 为 0。
  9. **首次加载探针**（§4）：两轮的原始记录与汇总落盘到 `logs/task-15-firstload-r1.txt`、`logs/task-15-firstload-r2.txt`。
     - 第 2 轮：§2.3 与 §2.4 清单内的项在执行器上为 0；
     - 清单外的项逐条写出拥有者、分类（§2.2）、所在 CMD 类型与键数，按「待裁定」第 1 条的口径处置；
     - `startup:` 类型下只允许出现 `epic-book`、`experience-storage` 两个启动作业自己的加载。
     - **还原证明**：同 TASK-07 Required check 6 的三项——快照 sha1 前后相同；`snapcheck` 中不相等的只有本单元改动的文件；`grep "FirstLoadProbe\|GameplayProbe" Server/src` 为 0。之后以 `--no-incremental` 重新构建，再跑正式全量。
  10. **冷启动测量**（§6）：写入 `verification/measurement/task-15-coldstart.md`，标明「合成负载、每个变体两次运行、只作预热有效性的证据，不作 RQ-091 判定」。
      - 首次选角或首次进本仍高于 250 ms 时，不阻塞本单元，但必须给出剩余耗时的来源：探针第 2 轮里还剩哪些首次加载，或者确认不是内容加载（JIT、SQLite 冷页），交 M-1。
  11. **对账行**（AC-006）：附录 B「启动预热」行，写成「清单项 → 拥有者 → 预热入口 → 证据（W1／W2／探针第 2 轮）」。另写：
      - 不预热的项与理由（无界按键缓存的处置、没有在执行器上被触达的项）；
      - TASK-12 Result 第 5 条的关闭依据；
      - 留给 M-1 的部分。
- Dependencies：
  - TASK-14：基准 `bfbcf012`；S1 的宿主与装饰写法；两个启动作业在 `StartingAsync` 中的位置；全量与耗时基线 `logs/task-14-build-test.txt`。
  - TASK-12：事件组循环在 `StartAsync` 才开始。
  - TASK-07、TASK-08：探针工具（`verification/probe-tools/`）、还原证明的做法、差异核对脚本。
  - TASK-03：`Tool/GameplayLoad`、构成 B 的命令行、冷启动基线。
- Result review: required。
  - **清单是否完整，测试只能部分回答**：W1 只核对清单里写了的项，W2 只认得出 `Lazy<>` 字段，探针只覆盖负载与静态检索走到的路径。漏掉一项时，全量照样是绿的，要到 M-1 测量或实机上才以「执行器上数百毫秒的停顿」暴露出来。
  - **共享接口改动**：十余个 Abstractions 接口要各加一个成员，后续单元与测试替身都依赖这些接口。
  - **测量的判读依赖判断**：(f) 由单个慢事件决定（TASK-03「工具抖动」）。「剩余停顿不是内容加载」这种归因，测试暴露不出错误。
  - **启动与内存代价落在所有带档案的测试宿主上**：TASK-14 已因内存压力触发过一次停止条件。本单元的结果直接影响后续全量能否稳定。
- Task-specific authorization：
  - 首次加载探针（§4）、变异核对（check 6）与临时变体 P2（§5）期间，允许临时改动 `Server/src`。用后按 check 9 的三项还原证明处理；不提交，不 `git stash`。
  - 允许用 `git archive bfbcf012` 把基准导出到 scratchpad，在工作区外构建，作为变体 P0 的 Host。
  - 允许构建并运行 `Tool/GameplayLoad`，用法同 TASK-14 Required check 11 的授权：它拉起 Host 子进程，使用临时目录与 SQLite；不改工具源码，不施加 `yes` 负载。
  - 不安装 `dotnet-dump`、`dotnet-counters` 等需要下载的工具（TASK-14 主控方裁定）。RSS 用系统自带的 `ps` 采样。

## Detailed Plan

### 1. 今天的形状（基准 `bfbcf012`）

- `GameServerStartupService.StartingAsync` 在 `archive is not null` 时已经预热这些内容：装备索引、领主塔定义、独立掉落、额外掉落、怪物声明掉落、世界掉落、次元副本、角色基础属性、自动售货机、彩票、经验刻度。史诗书目录与配方由 `startup:…/epic-book` 启动作业加载，它在执行器上运行，但仍在开始监听之前。
  - 这些项各自记一条 Information；失败时异常直接抛出，宿主启动失败。
- 静态检索的现状：
  - Infrastructure 与 Application 中含 `Lazy<` 的行共 36 行，分布在 22 个文件。其中 7 个文件已在上面的预热里，15 个文件是 TASK-14 §5.2「本单元加入」表的内容（7 ＋ 15 ＝ 22）。
  - `Infrastructure/Content` 共 104 个文件，其中 92 个注入 `PvfArchiveProvider`。另有 43 个文件用 `??=` 在首次调用时建索引（例如 `PvfDungeonMapDefinitionSource.paths`），这一类**不是** `Lazy<>`，`grep "Lazy<"` 找不到它们。
- 执行器上的首次加载，今天会发生在这些地方：选角准备、进本、怪物死亡结算、事件 CMD（成就、称号簿）、婚礼定时作业等。TASK-03 测到的冷进程首次选角约 500 ms、首次进本约 1.1 s，这是端到端的总量，其中多少来自内容加载、多少来自 JIT 与 SQLite 冷页，今天没有分开测过。

### 2. 预热清单的确定方法

#### 2.1 两种方法各管什么

- **静态检索**给出候选全集与分类（§2.2）。它的局限：找得到「哪里会懒加载」，回答不了「执行器上会不会走到」。
- **首次加载探针**（§4）给出在负载下、执行器上实际发生的首次加载，以及所在的 CMD 类型。它的局限：只覆盖负载走到的路径；对缓存了档案引用、之后不再经过 getter 的内容源看不到（目前只有 `PvfDropItemReader` 在构造时接收档案，而它本身就是在一次经 getter 的加载里构造的）。
- 两者合起来定清单：静态检索得到的 A、B 类候选，全部预热；探针第 1 轮在执行器上抓到的，按分类决定预热或处置。

#### 2.2 分类（每个候选只归一类）

| 类 | 形状 | 例 | 处置 |
|---|---|---|---|
| A 整目录 | `Lazy<T>` 字段，或 `??=`／`is null` 建出的整份索引 | 成就目录、`paths ??= PvfListFile.Read(...)` | 预热 |
| B 有界按键 | 键集合有限且可枚举：枚举值，或档案声明的职业集合 | 技能规则（按职业）、掉落物品池（按 `DropItemFamily`） | 预热全部键 |
| C 无界按键 | 键是副本、地图、房间、物品 ID 等，键集合即档案中的数千到十余万条 | `PvfDungeonMapDefinitionSource.cache`、按物品的各类定义 | 按 §5 测量，按「待裁定」第 1 条处置 |
| D 不缓存 | 每次调用都读档案 | — | 不是预热对象；它的耗时属于该 CMD 的执行时长，记入对账行交 M-1 |

- A 类里的 `??=` 索引，只预热「探针第 1 轮在执行器上抓到」的那些（§2.4）。没有被抓到的，在对账行列出文件名，注明「负载与静态检索都未见执行器路径」。
- 理由：`??=` 共 43 个文件，全部加入口会让 Abstractions 改动翻倍，而其中多数在 GM、tooltip 等执行器外的路径上。RQ-091 原文「`Lazy` 内容源」是否包括这一类，见「待裁定」第 2 条。

#### 2.3 起始清单（TASK-14 §5.2「本单元加入」，在基准上逐项核对过存在）

| 内容 | 类 | 拥有者 | 预热入口落在哪里 |
|---|---|---|---|
| 成就目录 `PvfAchievementDefinitionSource` | A | `AchievementRuleService` | 规则服务 |
| 称号簿目录（同文件第二个类） | A | `TitleBookRuleService` | 规则服务 |
| 技能规则 `CharacterSkillRuleService.jobs`，以及经它填满的 `PvfSkillPointDefinitionSource.catalogs` | B | `CharacterSkillRuleService` | 规则服务，接收职业集合（§3.3） |
| 金币携带上限 `PvfGoldCarryLimitSource.bands` | A | `CarriedGoldRuleService` | 规则服务 |
| 会员服务的 10 个 `Lazy` | A | `PremiumServiceRuleService` | 规则服务 |
| 任务助手人偶 | A | `QuestHelperPuppetRuleService` | 规则服务 |
| 地下城经验进度策略 `ExperienceRuleService.policy` | A | `ExperienceRuleService` | 规则服务 |
| 掉落物品池 `PvfDropItemPoolSource._pools` | B | 7 个消费者共用一个内容源 | 预热入口放在 `DropRuleService`，枚举 `DropItemFamily` 的全部值 |
| 金银号角 | A | `GoldSilverHornRuleService` | 规则服务 |
| 地狱奖励 | A | `HellRewardRuleService` | 规则服务 |
| 守望之塔目录 | A | `SaoTowerRuleService` | 规则服务 |
| 婚姻典礼 | A | `MarriageCeremonyRuleService`（另有 `LovePoints`、`MarriageChurchRuleService` 共用） | 规则服务 |
| 婚姻求婚 | A | `MarriageProposalRuleService` | 规则服务 |
| 婚姻胶囊 | A | 没有规则服务拥有者（`CapsuleClearSubscriber`、`GrowthCapsules`） | 内容源接口（§3.2） |
| 婚姻住宅 | A | 没有规则服务拥有者（`HousingOperations`、`HousingReadView`） | 内容源接口（§3.2） |

#### 2.4 探针补入

- 探针第 1 轮在执行器上抓到、不在 §2.3 表里的 A、B 类项，补入清单，拥有者与入口按 §3.2 的规则选。
- **上限**：补入的拥有者超过 10 个时，先停下回报主控方，再动 Abstractions。理由：入口一多，就该考虑统一的预热契约，而不是逐个加成员（「待裁定」第 4 条）。
- C、D 类不补入，按 §5 与对账行处置。

### 3. 预热的实现

#### 3.1 位置与次序

- 新增 `ContentWarmup`（GameServer，internal），`StartingAsync` 在 `archive is not null` 分支末尾、`experience-storage` 作业之后调用它。已有各项不挪进去，它们的失败语义不变。
- **在执行器外**：`StartingAsync` 不在作业里运行，`ContentWarmup` 也不入队（check 8 的 grep）。两个启动作业照旧各自入队。
- **在开始监听之前**：`StartingAsync` 在任何托管服务的 `StartAsync` 之前完成。Kestrel、TCP 监听、事件组循环（TASK-12）、定时源都在 `StartAsync` 才开始。所以预热一定先于第一条玩法 CMD 与第一条事件作业，TASK-12 Result 第 5 条据此关闭。W1 证明清单内各项在宿主就绪时已加载。
- **顺序**：先放便宜的整目录项，最后放掉落物品池（装备族要遍历约 11.8 万个档案文件）。每项开始前检查取消令牌。

#### 3.2 经谁预热

- 有规则服务拥有者的，经规则服务进行（AGENTS.md：内容源只由规则服务注入）。
- 没有规则服务拥有者的（婚姻胶囊、住宅），在内容源接口上加入口，由 `ContentWarmup` 直接调用。先例：`StartingAsync` 已经直接调用 `IEquipmentDefinitionSource`、`IVendingMachineDefinitionSource`、`ILotteryDefinitionCache`。这只是启动组件读内容，不改变运行期的注入面（「待裁定」第 5 条）。
- 一个内容源有多个规则服务消费者时，只经一个拥有者预热一次，在 §2.3 表里写明是哪一个。

#### 3.3 入口的形状

- 一般情形：`int Initialize()`，与 `IWorldDropRuleService.Initialize` 相同。它触达该服务持有的全部 `Lazy`，返回加载的条数，不改其他成员。
- 技能规则：`int Initialize(IReadOnlyCollection<byte> jobs)`。职业集合由 `ContentWarmup` 从 `ICharacterStatRuleService.SupportedCareers` 取出职业号并去重（基础属性已先预热，集合来自档案声明）。规则服务显式接收 facts，不自己去找职业集合，与 AGENTS.md「规则服务显式接收职业」一致。
- 掉落物品池：`DropRuleService.Initialize()` 枚举 `Enum.GetValues<DropItemFamily>()`。
- 接口成员**不给默认实现**，与已有的四个 `Initialize` 一致。测试替身因此要补一个成员（§8）。备选是默认实现返回 0，它不改任何测试文件，但生产实现漏写时不报编译错误，只能靠 W1 发现。

#### 3.4 记录

- 每项记一条 Information：`Content warmup: {Item} {Count} in {ElapsedMs} ms`。
- 全部结束后记一条汇总：`Content warmup finished: {Items} items in {ElapsedMs} ms`。check 2 的宿主启动计数与 §6 的启动时长增量都取自这条汇总。

#### 3.5 失败语义（与今天一致）

- **今天**：这些项是懒加载。档案里某项坏了，宿主照常启动，只有用到它的请求失败；`Lazy<T>` 的默认模式（`ExecutionAndPublication`）会缓存这个异常，之后每次访问都抛同一个异常。
- **本单元**：对新增各项逐项 `try`。除取消以外的异常，记一条 Warning `Content warmup failed: {Item}`（带异常），然后继续下一项，宿主照常启动。
  - `Lazy<T>` 与 `ConcurrentDictionary<,Lazy<T>>` 已缓存了该异常，之后的请求照今天的样子失败；
  - `??=` 索引不缓存异常，请求时会重试一次，这也和今天相同。
  - 所以，对请求方可见的结果与今天一致。F1 守着这一点。
- 取消（`OperationCanceledException`，且令牌已取消）照常抛出。
- `archive is null` 时不预热，与今天相同。
- 已有各项照旧直接抛出，不纳入这里的处理。
- 备选：新增项失败也让启动失败。见「待裁定」第 3 条。

### 4. 首次加载探针（临时，不提交）

- **挂点**：`PvfArchiveProvider.Archive` 的 getter 第一行，调用 `FirstLoadProbe.Hit()`。探针放在 `Dfo.Server.Abstractions/Execution/FirstLoadProbe.cs`，与 `GameplayProbe.cs` 同位置，补丁与还原复用 `probe-tools/patch.py` 的备份与 `snapshot.py`／`snapcheck.py` 的做法。脚本与模板放在 `verification/probe-tools/firstload/`。
- **只记执行器上的调用**，与 `GameplayProbe.Hit` 相反：`GameplayJob.IsOnExecutor` 为假就立即返回。所以预热与其他执行器外的加载几乎没有开销。
- **去重**：
  - 先取 `new StackFrame(1)` 的方法，与 `GameplayJob.Current` 的作业身份一起作为键；
  - 同一作业里、同一调用方法只在第一次取完整栈。一次装备族遍历会调用 getter 约 11.8 万次，逐次取完整栈会严重拖慢运行。
- **每条记录**：
  - `GameplayJob.Current.CommandType`；
  - 栈上最内层的 `Dfo.Server.Infrastructure.Content` 类型与方法，作为分类键；
  - 最内层的 `Dfo.Server.Application` 规则服务帧；
  - 该键在本进程内第几次被看到；
  - 时间戳，相对于进程启动。
- **汇总**按「内容源类型 → 分类（§2.2）→ CMD 类型 → 次数与不同作业数」，写出加法。
- **两轮**，都用 §6 的负载（构成 B ＋ 构成 A 各一次，同一命令行）；探针运行**不计入**任何时长数字：
  1. 第 1 轮在变体 P0（基准构建）上运行，得到执行器上首次加载的原始清单。用它校对 §2.3、确定 §2.4 补入哪些，并给出 C 类的键数与所在 CMD；
  2. 第 2 轮在本单元的构建上运行，要求同 check 9。
- 加跑构成 A，是因为只有它覆盖建角、回选角再选、跨区。基线上 A 被 SQLite 30 秒停顿主导（TASK-03），这里只用来扩大路径覆盖，不读它的时长。
- 还原证明同 check 9。

### 5. 无界按键缓存的测量方案

三个变体，都是 Release 构建，同一台机器，同一个 PVF：

| 变体 | 内容 | 是否提交 |
|---|---|---|
| P0 | 基准 `bfbcf012`，`git archive` 导出后在工作区外构建 | 否 |
| P1 | 本单元：§2.3 ＋ §2.4 的预热 | 是 |
| P2 | P1 ＋ 临时代码，把 C 类按家族全部预热：副本族（副本、地图、房间角色、房间掉落）与物品族，各家族单独计时 | 否，测完按 check 9 还原 |

- P2 的临时代码逐键加载：遍历档案中该家族的 `.lst`。每个家族记录键数、总耗时、单键耗时的 p50／p99／max。只用于测量，不讲究形状。
- 测出的量：
  1. **启动时长增量**：工具输出的 `host ready after … ms`，以及 `Content warmup finished` 汇总行。比较 P1 − P0 与 P2 − P1（按家族拆开）。
  2. **常驻内存增量**：Host 就绪时与冷启动窗结束时的 RSS（`ps -o rss= -p <pid>`，每秒采样），比较 P1 − P0 与 P2 − P1（按家族拆开）。这直接关系到带档案的测试宿主的内存压力。
  3. **冷启动占比**：对首次 `PrepareSelectedCharacterStateCommand`、首次 `EnterDungeonCommand` 以及冷启动窗内的最长停顿，分别求：
     - P0 − P1：有界预热消掉的部分；
     - P1 − P2：C 类首次加载占的部分；
     - P2 的剩余：非内容加载（JIT、SQLite 冷页等）。
     - 每个变体跑两次。(f) 由单个慢事件决定，两次之间差别大时，写出两次的值，不取平均。
  4. **C 类在负载中的触达面**：探针第 1 轮给出每个家族在执行器上首次加载的键数、所在 CMD 类型、单个 CMD 内首次加载的最大键数。
- 这四组数写进 Result 与 `task-15-coldstart.md`，然后按「待裁定」第 1 条的口径处置。主控方在执行前已裁定口径的，执行者照口径直接做完；没有裁定的，执行者测完停下回报，等裁定后再改 P1。

### 6. 冷启动测量

- 命令行同 `verification/measurement/README.md` 的构成 B：`--weights 0.5,0.5,0,0,0 --entry-concurrency 1`，只把 `--host` 换成对应变体的 `Dfo.Server.Host.dll`。工具 dll 的 SHA-256 与 `raw/tool-version.txt` 对照，写进记录。
- 日志级别保持默认 Information，与基线相同；记录每次运行的 `hostLogLines`。
- **记录**（每个变体两次）：
  - Host 就绪时长、预热汇总行与各项耗时；
  - 第一次 `PrepareSelectedCharacterStateCommand`、第一次 `EnterDungeonCommand` 的服务端时长（`CommandLoggingBehavior` 日志行）；
  - (f) 冷启动前 60 秒的最长单次停顿（判定序列，含截尾），以及它是哪个请求、在什么时刻；
  - RSS（§5）。
- **对照表**：TASK-03 基线（不重跑）／P0／P1／P2。按 README 的口径注明：改造后多段 CMD 的首条回包按 VC-1 推迟到 CMD 结束，所以选角的端到端数字预期比基线大，这是口径决定的。
- **(f) 预热部分的判断**：
  - P1 中首次选角、首次进本的内容加载份额（P0 − P1，以及 P1 − P2）都已消除或已按裁定处置；
  - 并且探针第 2 轮里清单内项为 0。
  - 两条都满足，就认为「预热部分」完成。剩余停顿是否 ≤ 250 ms 由 M-1 判定；高于 250 ms 时，按 check 10 给出来源。

### 7. 新增测试

不新增拉起宿主的用例。W1 搭在 S1 已经拉起的那个真实内容宿主上；W2 与 F1 不起宿主、不读档案。

| 条 | 位置 | 驱动 | 断言 |
|---|---|---|---|
| W1 | `ContentWarmupTests.AssertWarmed(IServiceProvider)`，由 S1 在原断言之后调用（`[PvfFact]`，无档案时与 S1 一起跳过） | S1 的宿主完成 `StartingAsync` 后 | 对清单表（测试内的唯一一份，附录 B 引用它）的每一项：反射取拥有者实例上的字段。A 类断言 `Lazy.IsValueCreated` 为真，或 `??=` 字段非空；B 类断言键集合覆盖全部键（职业 ＝ `SupportedCareers` 的职业号，族 ＝ `DropItemFamily` 全部值），且每个 `Lazy` 都已创建 |
| W2 | `ContentWarmupTests`，`[Fact]` | 反射 Application 与 Infrastructure 程序集中全部类型的实例字段 | 类型为 `Lazy<>` 或 `ConcurrentDictionary<,Lazy<>>` 的字段，必须恰好落在清单表中，或落在带非空理由的豁免表中（C 类与执行器外的项）。清单表里的每一项在程序集中都存在，没有过时项 |
| W2 反例 | 同上 | 把分类函数用于测试程序集里一个带未登记 `Lazy<int>` 字段的样例类型 | 被判为违规（RQ-072 式：证明判据会拒绝） |
| F1 | `ContentWarmupTests`，`[Fact]` | 自建只含替身的小容器调用 `ContentWarmup`：一项 `Initialize` 抛 `InvalidOperationException`，其余正常；另一条用例用已取消的令牌 | 前者：调用正常完成，后续各项照常执行，日志恰有一条 `Content warmup failed`，带该项名字与异常。后者：抛 `OperationCanceledException` |

- **为什么 W1 不单独起宿主**：TASK-14 定向复核之后的裁定要求避免再增加重型宿主用例。S1 已经用 `realContent: true` 拉起宿主并跑完 `StartingAsync`，再起一个只会加重内存压力，不会多证明任何东西。代价是 S1 的方法多了一组断言。S1 失败时，要看失败行属于哪一组，Result 里写明。
- W2 只认 `Lazy<>` 形状。`??=` 索引不能可靠地按结构识别，它们由探针与 W1 的清单项覆盖（§2.2）。
- 先写测试再改生产代码。W1、F1 在改动前应当红（编译失败也算红），Result 记录红的形态。W2 检查的是字段集合与清单表是否对得上，生产改动不增减 `Lazy` 字段，所以它在改动前后都应为绿；它的区分力由反例与 V11 证明。
- N 的预计：W2 2（含反例）＋ F1 2 ＝ 4。W1 是 S1 内的辅助，不增加方法数。以实际方法数为准。

### 8. 既有测试

- 实现了被加成员接口的测试替身：在基准上用 `grep` 按接口名找出实现类，执行时逐个列出。粗查的量级是 `IExperienceRuleService` 约 8 个文件，`ICarriedGoldRuleService` 约 3 个，其余 0～2 个。每个只补 `public int Initialize() => 0;`（技能规则带参数），不动任何断言。
- 替身被注册进宿主、而宿主又走到预热分支时，`ContentWarmup` 会调到替身的入口，返回 0。这些宿主里用到该服务的用例，本来就在用替身，行为不变。
- **带档案的宿主变慢**：表征宿主、`GmTestHost(realContent: true)`、打包 exe 用例都会跑预热。它们的启动耗时变化由 check 2 的耗时比对与预热汇总行计数说明。若因此出现超时形态的失败，先隔离复跑；属于预算问题的，交「待裁定」第 6 条，不擅自加开关。

### 9. 附录 B 的追加（`spec.md`）

在「启动预热的 `Lazy` 内容源」行的单元格末尾追加「**清单（TASK-15，基准 `bfbcf012`）**」，不改原文：
- 已有的预热项，以及 `epic-book` 启动作业中的史诗书目录；
- 本单元加入的各项：内容 → 拥有者 → 入口；
- 不预热的项与理由：C 类按裁定处置；`??=` 索引中未见执行器路径的，只写文件数并引用对账行，不逐个列；
- 判据：W1（`GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs` 中的 `ContentWarmupTests.AssertWarmed`）、W2（`ContentWarmupTests` 的结构判据）。
- 加一条约束：新增 `Lazy` 内容源必须同时改清单或豁免表与本行，W2 守着这一点（RQ-011 式）。

### 10. 风险与可见差异

1. **启动变慢**：所有新增加载移到开始监听之前。掉落物品池的装备族要遍历约 11.8 万个文件，可能是最大的一项。§5、§6 给出实际数字。
2. **测试宿主的时间与内存**：每次带档案的宿主启动都要付预热代价，Host.Tests 已有内存压力（TASK-14 Remaining 第 3 条）。check 2 采样 RSS，超出时按「待裁定」第 6 条处理。
3. **清单遗漏**：见 Result review 第 1 条。缓解办法是三条证据互补——W2 结构、探针两种负载构成、静态检索的 A／B／C／D 全表。
4. **失败语义**：坏内容从「请求时失败」变成「启动时记 Warning、请求时照旧失败」。启动日志多一条 Warning，算作可见差异，不改线上字节。
5. **剩余停顿可能不是内容加载**：消掉内容加载之后，首次选角若仍超过 250 ms，剩下的多半是 JIT 与 SQLite 冷页，不在本单元范围内。按 check 10 写明来源，交 M-1。

### 11. 执行顺序（一次提交）

1. **第 0 步**：导出并构建 P0；构建 `Tool/GameplayLoad`，核对工具 SHA-256。
2. **探针第 1 轮**（P0 上，构成 B 与 A）→ 汇总 → 校对 §2.3，确定 §2.4 的补入项（超过 10 个拥有者就停下回报）。
3. **P0 冷启动测量 ×2**（不带探针）。
4. **测试先行**：写 W1、W2（含反例）、F1，记录红的形态（`logs/task-15-red.txt`）。
5. **生产代码**：各拥有者的入口 → 测试替身补成员 → `ContentWarmup` → `StartingAsync` 中的调用。构建，跑 Host.Tests 与 Application.Tests。
6. **P1 冷启动测量 ×2**；构建 P2，测量 ×2，然后还原。此时若口径未裁定，停下回报 §5 的四组数。
7. 按裁定调整 P1（如需），然后跑探针第 2 轮，做还原证明。
8. 变异核对 V9～V12，然后以 `--no-incremental` 重新构建。
9. 正式全量（含 Host.Tests 的 RSS 采样）→ 隔离复跑 → 新增测试 ×10 → 测试改动核对 → 边界 grep → 附录 B 追加 → 对账行 → 写 Result。

### 12. 为什么除可见差异外行为不变

- 预热调用的是运行期首次访问时本来就会调的同一段加载代码，得到同一份不可变内容，只是时间提前到开始监听之前。
- 加载逻辑与缓存实现都不改，新增的只是触达它们的入口。
- 失败的可见结果与今天相同（§3.5）。线上字节由表征测试与既有线级测试守着。

## 待裁定

1. **无界按键缓存（C 类）的预热口径**（TASK-14 裁定第 10 条：先测量再定）。建议在执行前定下以下规则，执行者照规则直接做完；不定的话，执行者在 §11 第 6 步停下回报：
   - **预算**：本单元全部预热相对 P0 的 Host 就绪时长增量 ≤ 10 秒，就绪时 RSS 增量 ≤ P0 的 25%。
   - **C 类的规则**：P1 下，首次选角与首次进本的服务端时长都 ≤ 100 毫秒（RQ-091 中 p99.9 的那条线）时，C 类全部保持懒加载。否则按「P1 − P2 份额」从大到小逐个家族加入全量预热，直到两者都 ≤ 100 毫秒或预算用完。
   - **仍保持懒加载的 C 类家族**：在附录 B 中写明「按键缓存，首次加载发生在执行器上」，附上单键耗时 p99／max 与单 CMD 最大键数，交 M-1 判定。这实际上是对 RQ-091「全部 `Lazy` 内容源」的一个口径解释：按键缓存的单键加载不算「内容源首次加载」，只要单键代价不威胁 250 毫秒的单次停顿。请主控方确认这个解释，或者要求 C 类无条件全部预热（那样预算上限就要另定）。
2. **RQ-091「`Lazy` 内容源」的范围**：
   - 本方案按「任何在首次调用时整份加载的内容」理解，所以包括 `??=` 索引；
   - 但只预热探针在执行器上抓到的那部分，其余列文件数。
   - 备选甲：只按字面，限于 `Lazy<>`，范围更窄，但已知的 `??=` 首次加载会留在执行器上。
   - 备选乙：43 个文件全部预热，Abstractions 改动约再翻一倍。
   - 请确认。
3. **新增预热项的失败语义**（§3.5）：建议逐项捕获、记 Warning、继续启动，这样请求方看到的结果与今天一致。备选：失败即启动失败，与已有预热项一致，但档案里某个今天无人使用的坏项会让宿主起不来，这是附录 G 之外的行为改变。请确认。
4. **探针补入的上限**（§2.4）：超过 10 个拥有者时停下回报。届时可以考虑统一的预热契约（例如一个 `IContentWarmup` 接口，由组合根集中注册），代替逐个加 `Initialize`。请确认上限，或者现在就改用统一契约。
5. **没有规则服务拥有者的内容源**（婚姻胶囊、住宅）：由启动组件直接调用内容源接口上的入口（有先例，§3.2）。备选是为它们新建规则服务，那属于结构改动，超出本单元。请确认。
6. **测试宿主的预算超出时怎么办**：若 check 2 显示 Host.Tests 的内存峰值或耗时明显上升（例如峰值 RSS 高于 TASK-14 的 15.2 GB，或出现新的 > 30 秒停顿），可选：
   - 甲：接受，并交 TASK-16 已定的「重型宿主数量与内存」检查一并评估；
   - 乙：加配置项，让测试宿主默认不预热 C 类全量部分（不影响 A、B 类）。
   - 不建议对 A、B 类加开关：那样表征宿主就不再走生产的预热路径。
   - 建议甲，请确认。
7. **接口成员不给默认实现**（§3.3）：测试替身要逐个补一行。备选是默认实现返回 0，零测试改动，但生产实现漏写时只能靠 W1 发现。建议本方案，请确认。

## 主控方裁定（执行前）

1. **C 类口径**：按建议规则执行。
   - 预热总增量不超过「就绪时长 +10 s、RSS +25%」。
   - P1 下首次选角与首次进本都 ≤ 100 ms 时，C 类保持懒加载；否则按占比逐个家族加入预热，直到达标或预算用完。
   - 仍然懒加载的 C 类，逐项写进附录 B 预热行的「交 M-1」清单。
   - 对 RQ-091 的解释：「执行器上会触达的全部 Lazy 内容源」指整目录类内容源（A 类）和有界按键内容源（B 类）；无界按键缓存的单键首次加载，不算「内容源首次加载」。这一解释在 M-1 用 (f) 冷启动停顿做最终判定，不达标时回到用户。
2. **`??=` 索引的范围**：接受本方案。`??=` 索引纳入范围，但只预热探针抓到或静态检索确认会在执行器上首次触达的部分。
3. **失败语义**：逐项记 Warning 后继续。`Lazy` 会缓存异常，请求方看到的结果与今天一致。
4. **探针补入上限**：超过 10 个拥有者时停下回报，不自行改用统一契约。
5. **婚姻胶囊、住宅**：由启动代码直接调用内容源接口上的入口，有先例可循。
6. **测试宿主的代价**：接受，并增加一条停止条件。每个带档案的测试宿主都要付预热的代价，以下任一情况出现即停下回报：
   - 正式全量各项目逐条耗时之和，比 TASK-14 增加 20% 以上；
   - Host.Tests 的 RSS 峰值超过 TASK-14 测得的上限 15.2 GB。
   
   回报时附上每个带档案宿主的预热耗时分布和最贵的前 5 项。不得自行给 A、B 类加测试开关。整体评估在 TASK-16 的重型宿主检查中一并做。
7. **接口成员不给默认实现**：测试替身逐个补一行，接受。

## Progress（执行者，2026-10-22：§2.4 停止条件触发，停下回报）

**已完成**（§11 第 0～3 步；`Server/src`、`Server/tests` 一行未改）：
- 第 0 步：基准导出到 scratchpad `t15/p0`，工作区外 Release 构建 Host；探针副本 `t15/p0probe`（补丁只打在副本上）。工具 dll SHA-256 与 `raw/tool-version.txt` 相同。
- 第 2 步：首次加载探针第 1 轮（P0，构成 B ＋ A，各一次完整运行，errors 0）。原始汇总与分类：`verification/logs/task-15-firstload-r1.txt`；探针工具：`verification/probe-tools/firstload/`（`FirstLoadProbe.cs`、`patch.py`、`summarize.py`）。
- 第 3 步：P0 冷启动测量 ×2，记录在 `verification/measurement/task-15-coldstart.md`（标明进行中、只有 P0）。

**停止原因**：探针第 1 轮在执行器上抓到的 §2.3 之外的 A／B 类项，按拥有者归并为 11 个（祭坛、Joust、比武、建角、物品落位、绝望之塔、城镇落点、世界内容入场、任务目录、副本准入、房间规则；1 × 11 ＝ 11），超过 §2.4 与裁定第 4 条的上限 10。逐项见 r1 日志第四节。还没有动 Abstractions。

**另见 r1 日志的几条发现**（给裁定用）：
- 其中 3 个（物品落位、副本准入、房间规则）只是 C 类内容源里的路径子索引；若按 C 类处置，计数为 8。
- §2.3 里的 `ExperienceRuleService.policy` 在基准上已经随 `CharacterScale()` 在启动时加载。
- D 类（每次都读档案）有 3 个：周签到（几乎每条玩法 CMD 都读，单作业最多 74 次读）、超额技能契约、每日补给规则（每次选角重读 stackable.lst 与装备路径表）。它们不是预热对象，耗时算在 CMD 里，交 M-1。

**当前 owned diff**：只有新增的未跟踪文件——上面三处证据与工具，以及主控方的 `tasks/TASK-15.md`。

**下一步**（等裁定）：上限放宽到 11（或者 3 个子索引按 C 类处置），或者改用统一预热契约。之后从 §11 第 4 步（测试先行）接着做，第 2 步的结果可以直接用。

## 第 2 版修订（主控方，执行中裁定：探针补入的拥有者 11 个，超过上限 10）

本节优先于正文中与之冲突的内容，包括 §2.4、§3 关于逐个接口加 `Initialize()` 的做法，以及裁定第 7 条。

1. **采用丙：统一预热契约。**
   - 在 `Dfo.Server.Abstractions` 新增一个预热参与者接口，形如 `IContentWarmup { string Name { get; } int Warm(); }`，返回加载的条目数。名称和放置位置由执行者按后缀约定决定，在 Result 中说明。
   - 每个需要预热的拥有者（规则服务或内容源）在实现类上额外实现这个接口，并在组合根中以 `IContentWarmup` 注册同一个单例。
   - `ContentWarmup` 解析 `IEnumerable<IContentWarmup>`，逐个调用。
   - 不给原有的 Abstractions 接口加成员，测试替身不受影响。裁定第 7 条随之作废。
   - 已有的 7 项预热保持原样，不强制迁到新契约。
2. **范围**：§2.3 的各项，加上探针第 1 轮抓到的 11 个拥有者（含第 5、10、11 项的路径子索引），全部实现契约并预热。C 类口径照旧按裁定第 1 条执行。
3. **失败语义不变**：逐项捕获，记 Warning 后继续；取消照常抛出。
4. **判据**：W2 改为结构判据。Application 与 Infrastructure 中，凡是持有 `Lazy<>` 或 `??=` 首次索引的拥有者类型，要么实现预热契约，要么在带理由的豁免表中登记，并配一条反例用例。检测 `??=` 可以用源码检索或反射近似，具体做法写进 Result。
   - W1 改为：遍历全部 `IContentWarmup`，断言在真实内容宿主上每一项都成功，且返回的条目数大于 0。豁免项除外。
5. **D 类**（周签到、超额技能契约、每日补给规则）：不预热，原样交给 M-1。
   - 其中周签到几乎每条玩法 CMD 都会读档案，单个作业最多读 74 次，要在 Remaining 中单列为 M-1 的性能关注点。
6. **继续**：沿用第 1 轮的探针结果，从 §11 第 4 步（测试先行）接着做。探针第 2 轮用来证明执行器上的首次加载归零：只允许残留 C 类和 D 类，并按类别逐项列出。
7. **停止条件**：裁定第 6 条的耗时与 RSS 两项停止条件照旧有效。

## Progress（执行者，2026-10-22，第 2 版修订之后：裁定第 6 条的耗时停止条件触发，停下回报）

**已完成**（按第 2 版修订执行；未提交）：
- 契约：新增 `Dfo.Server.Abstractions/Content/IContentWarmup.cs`，含 `IContentWarmup`（`Name`、`ValueTask<int> WarmAsync(CancellationToken)`）与注册扩展 `AddContentWarmup<TService>()`。注册的是 TService 背后的同一个单例；如果测试替身占了这个位置，就注册一个「replaced, not warmed」占位，它返回 0。
- `ContentWarmup`（GameServer，internal）：逐项捕获、记 Warning；取消照常抛出。在 `StartingAsync` 的档案分支末尾调用。另在 GameServer.csproj 加了 `InternalsVisibleTo Dfo.Server.Host.Tests`。
- 31 个参与者：Infrastructure 23 个内容源；Application 7 个规则服务，以及新增的 `CharacterSkillRuleWarmup`。清单见 spec 附录 B「启动预热」行末尾的追加。
  - 其中 `epic-book-rules` 是 W1 发现的：`epic-book` 启动作业只读目录，不读配方。
- 测试：`ContentWarmupTests`，含 W2、W2 反例、F1 两条，另有 W1 辅助；S1 末尾加一行 W1 调用。红的形态见 `logs/task-15-red.txt`。
- 变异 V9～V12 全部按预期变红，并逐字节还原：`logs/task-15-mutations.txt`。
- 探针第 2 轮：清单内的项在执行器上为 0，只剩 C 类、D 类和 epic-book 作业自身的加载：`logs/task-15-firstload-r2.txt`。
- 冷启动 P0／P1／P2 各两次：`verification/measurement/task-15-coldstart.md`。

**停止原因**：正式全量（`logs/task-15-build-test.txt`）中，共同用例逐条耗时之和 2685.3 → 3579.0 s，增加 33.3%，超过 20%。
- 其中 Host 953.6 → 1833.9 s，＋92.3%；其余项目在 −4.8% 到 +7.4% 之间。
- 逐条对比见 `logs/task-15-slow.txt`。

**全量本身的结果**：
- 总数 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 969 ＋ 173 ＋ 4502 ＋ 180 ＝ 8134 ＝ 8130 ＋ 4 － 0，只有 Host 多了 4 条（176 → 180）。
- 通过 8112、失败 2（就是那 2 条 `DimensionDungeonTests`）、跳过 20：8112 ＋ 2 ＋ 20 ＝ 8134。
- 挂起转储 0。

**交给裁定的数据**：
1. 全量中跑了预热分支的宿主启动有 160 次。
   - 每次预热总耗时 min 938、p50 4743、p90 12619、max 18013 ms，合计 985198 ms；
   - 同一批宿主在 Host.Tests 单独跑时是 min 946、p50 3932、p90 7454、max 9334 ms，合计 651423 ms；
   - 单个 Release Host 上（P1）约 1.4 s。
2. 最贵的前 5 项（按全量平均）：

| 项 | 平均 | 最大 |
|---|---:|---:|
| drop-rules（装备族物品池，遍历约 11.8 万个档案文件） | 4368 ms | 14559 ms |
| altar-dungeons（地图按副本索引，遍历全部地图元数据） | 698 ms | 3189 ms |
| character-skill-rules | 398 ms | 2073 ms |
| joust | 210 ms | 836 ms |
| quests | 185 ms | 909 ms |

   joust 在测试内容替身下失败 130 次（「马战物品 490002916 未声明」），与请求方看到的结果一致，但每次都留一条 Warning。
3. Host.Tests 单独全量（`logs/task-15-isolated.txt`）：180 条全部通过，3.60 min；逐条耗时之和比 TASK-14 全量中的 Host 部分多 16.3%；testhost RSS 峰值 10038784 KiB（约 9.6 GiB），不超过 15.2 GB。
4. 全量期间的内存：
   - testhost RSS 最大采样 14951808 KiB，约 14.26 GiB，按 10⁹ 计约 15.3 GB。TASK-14 的「15.2 GB」口径未写明单位，与之相比的结果**不足以确定**；
   - swap 已用从 6.8 GB 涨到 15.8 GB，swap 总量随之扩大。
   - 按裁定的排查顺序：
     - 本单元路径：预热在全量下是单宿主时的 3～13 倍，最贵的是 drop-rules 的档案遍历；
     - 换页：swap 增长 9 GB，与全量下 Host 变慢同时出现。
     - 线程栈没有工具可取。
   - 「全量下 Host 慢 92%、单独跑只慢 16%」的差距，归因于换页还是 CPU 争用，**未判定**。

**当前 owned diff**：`git status` 中 35 个已改文件，都在第 2 版修订的边界内：
- `Server/src` 下 33 个（Infrastructure 22 个内容源 ＋ InfrastructureModule；Application 7 个 ＋ ApplicationModule；GameServer 2 个）；
- Host.Tests 的 `GmGameplayRouteTests`；
- `spec.md`。
新增文件：`IContentWarmup.cs`、`ContentWarmup.cs`、`CharacterSkillRuleWarmup.cs`、`ContentWarmupTests.cs`，以及 verification 下的日志、测量与工具。

**未做的检查**：
- Networking.Tests 隔离复跑；
- 新增测试 ×10；
- 测试改动核对（diffcheck）；
- 边界 grep；
- Result。

**下一步**（等裁定）：
- 若接受代价：补做上面这些检查，然后写 Result；
- 若要降低测试宿主代价：需要裁定允许的手段。裁定第 6 条禁止给 A、B 类加测试开关。

## 主控方裁定（第二次停止之后：全量耗时 +33.3%）

**选择乙。修改原裁定第 6 条，允许为测试宿主加一个配置开关。**

1. **配置开关**：新增配置键 `ContentWarmup:Enabled`，默认值 `true`。
   - 生产配置文件与 Host 的默认值都不改，生产始终预热。
   - 开关只控制 `ContentWarmup` 那一步（统一契约的参与者）。已有的 7 项预热和启动作业不受影响。
   - `ContentWarmup` 在开关关闭时记一条 Information 日志，写明预热被配置跳过。
2. **测试基础设施默认关闭**：在 `Networking.Tests` 的 `TestInfrastructure` 和 `CharacterizationHost`、Application/Protocol 的测试宿主组合、Host.Tests 的进程内宿主中，默认设为 `false`。
3. **以下测试必须保持开启**：
   - W1 预热判据（S1 末尾）与 `ContentWarmupTests`；
   - 全部表征测试，使它们的宿主与生产形态一致，金样本不变；
   - Host.Tests 中拉起真实打包进程的测试（`EndToEndHostHarness` 系列），它们沿用生产默认配置；
   - TASK-10 的停机测试 S2。
   - 每类保持开启的测试都要在 Result 中列出。
4. **重新评估原禁令**：原裁定第 6 条禁止加测试开关，理由是怕预热本身失去测试覆盖。现在 W1、表征测试和真实进程测试保持开启，这一理由已经消除。预热只改变首次加载发生的时机，不改变行为，所以开关不构成执行器旁路。在 Result 中写明「开关关闭时，执行器上的首次加载与 TASK-15 之前相同」。
5. **测试**：
   - 开关关闭时不调用任何参与者；
   - 开关默认值为 `true`；
   - 做一条变异：把默认值改为 `false`，应有用例变红。
6. **重新测量停止条件**：
   - 正式全量中，共同用例逐条耗时之和对 TASK-14 的增量不超过 20%；
   - testhost 的 RSS 峰值以 KiB 记录，并与 TASK-14 原始日志中同一采样方式得到的峰值对比，单位写明。
   - 仍然超标时停下回报。
7. **C 类与首次选角**：接受 C 类全部保持懒加载。首次选角 101.7 和 104.3 ms 中超出 100 ms 的部分，在 P2 下同样存在，不来自内容加载，交 M-1 判定，并写入附录 B 的「交 M-1」清单。
8. **joust 的 130 次 Warning**：测试内容替身缺少物品 490002916。开关关闭后，大部分宿主不会再出现这条 Warning；仍然保持开启的测试里如果出现，在 Result 中如实记录，不必修改替身。
9. **之后补做剩余检查**：Networking 隔离复跑、新增测试 ×10、测试改动核对、边界 grep，然后写 Result。

## Result

### Outcome

统一预热已经落地。主要结论：

- 31 个参与者实现 `IContentWarmup`，由 `ContentWarmup` 在 `StartingAsync` 中、执行器外、开始监听之前逐项加载。
- 探针第 2 轮：清单内的项在执行器上为 0，剩下的只有 C 类、D 类和 epic-book 启动作业自身的加载。
- 冷启动（合成负载）：
  - 首次选角 493.3／534.7 → 101.7／104.3 ms；
  - 首次进本 76.0／839.0 → 56.7／57.6 ms；
  - (f) 527.6／860.5 → 133.6／137.6 ms。
- 按第二次停止后的裁定加了开关 `ContentWarmup:Enabled`。开关落地后重跑的正式全量里，共同用例逐条耗时之和比 TASK-14 只多 1.2%，Host.Tests 的 RSS 峰值也低于 TASK-14，两项停止条件都不再触发。
- 正式全量多出 1 条超时形态的失败：`LauncherHttpHostStartupTests` 的 game: True，满载下撞上 15 s 预算。隔离 5 轮全部通过，复跑的全量里也通过。归为测试预算问题，交主控方，见 Remaining 第 1 条。
- RQ-091 的达标判定归 M-1。

### Changed

**生产**：`Server/src` 33 个已跟踪文件 ＋ 3 个新文件，边界见 `logs/task-15-boundary.txt`。
- 新契约 `Dfo.Server.Abstractions/Content/IContentWarmup.cs`：
  - 接口有 `Name` 和 `ValueTask<int> WarmAsync(CancellationToken)`；
  - `ContentWarmupRegistration.AddContentWarmup<TService>()` 把 TService 背后的同一个单例注册为参与者；该位置被测试替身占了时，注册一个返回 0 的「replaced, not warmed」占位。
- `Dfo.Server.GameServer/ContentWarmup.cs`（internal）：
  - `IsEnabled` 读 `ContentWarmup:Enabled`：缺省为 true，只有显式 false 才关，非法值抛异常；
  - `RunAsync`：关闭时记一条 Information 后返回，不解析任何参与者；开启时逐项执行，失败记 Warning 后继续，取消照常抛出。
- `GameServerStartupService`：档案分支末尾加了一句调用；已有各项与两个启动作业都没动。`Dfo.Server.GameServer.csproj` 加了 `InternalsVisibleTo Dfo.Server.Host.Tests`。
- 参与者（入口名 → 拥有者）。原有接口没有加任何成员。
  - Infrastructure 23 个：achievement-catalog、title-book-catalog、gold-carry-limits、gold-silver-horn、hell-reward、sao-tower、marriage-ceremony／proposal／capsule／housing、despair-tower、towns、daily-activities、collection-boxes、quests、item-placement-index、initial-equipment、initial-skills、dungeon-index、dungeon-map-index、dungeon-room-object-index、dungeon-room-actor-index、altar-dungeons，都在对应的 `Pvf*` 内容源上；注册集中在 `InfrastructureModule.AddContentWarmups`。
  - Application 8 个：premium-service-rules、quest-helper-puppets、experience-rules、joust、tournament-dungeons、epic-book-rules、drop-rules，以及新类 `CharacterSkillRuleWarmup`（character-skill-rules）；注册集中在 `ApplicationModule.AddContentWarmups`。
  - 加法：23 ＋ 8 ＝ 31。
- 两处机械改动，加载逻辑不变：
  - `PvfItemPlacementDefinitionSource` 把 `??=` 两行抽成 `Index()`；
  - `PvfAltarDungeonContentSource` 把副本集合收成 `Dungeons` 数组。

**测试**：
- 新增 `Server/tests/Dfo.Server.Host.Tests/ContentWarmupTests.cs`，6 个方法：
  1. `W2_every_lazy_or_first_use_index_holder_is_warmed_or_exempt_with_a_reason`
  2. `W2_rejects_an_unlisted_lazy_field_and_an_unlisted_first_use_index`
  3. `F1_a_failing_item_is_logged_and_the_rest_still_warm`
  4. `F1_cancellation_is_not_swallowed`
  5. `Switched_off_the_warmup_resolves_and_calls_no_participant`
  6. `The_switch_defaults_to_on_and_only_an_explicit_false_turns_it_off`

  另有 W1 辅助 `AssertWarmedAsync`，它不是测试方法。N ＝ 1 ＋ 1 ＋ 2 ＋ 2 ＝ 6，Host 176 → 182。
- `GmGameplayRouteTests` 的 S1：原断言 → 原断言 ＋ 末尾一行 `await ContentWarmupTests.AssertWarmedAsync(host.Services);`；同时宿主调用加了 `configuration: { ContentWarmup:Enabled = true }`。
- `GmTestHost`、`ComponentLifecycleTests` 的配置字典各加 `["ContentWarmup:Enabled"] = "false"` 与一行注释。
- 测试替身不需要补成员：原有接口没有改。

**文档与证据**：
- `spec.md` 附录 B「启动预热」行：单元格末尾追加了清单、开关、证据、不预热的项、约束与交 M-1 的部分；原文未改。
- `verification/logs/`：task-15-firstload-r1／r2、red、mutations、build-test（正式全量）、build-test-r1（开关前的那次，只作对照）、slow、slow-r1、isolated、isolated-r1、new-x10、diff-check、boundary。
- `verification/measurement/task-15-coldstart.md`。
- `verification/probe-tools/firstload/`：`FirstLoadProbe.cs`、`patch.py`、`summarize.py`。

### Why（本地决定）

- **契约用异步形状**（`ValueTask<int> WarmAsync`）：裁定写的是「形如 `int Warm()`」，但不少内容源只有异步入口。
- **契约放在 `Abstractions/Content`**：
  - 它不属于 AGENTS.md 后缀表中的任何类别，所以取名 `IContentWarmup`，与裁定一致；
  - 后缀表在 AGENTS.md 里，不在本单元边界内，没有改。
- **参与者优先放在缓存的真正持有者上**：
  - 多数是内容源本身，这样返回的条目数准确，W1 也能直接核对持有者的字段；
  - 缓存在规则服务里的、或者键集合属于规则服务的，放在规则服务上：会员服务、Joust、比武、史诗书、经验、掉落；
  - 技能规则要别的规则服务提供的职业集合，所以新建 `CharacterSkillRuleWarmup`，显式接收 `ICharacterStatRuleService.SupportedCareers`，不改 `CharacterSkillRuleService` 的构造器。
- **新增 epic-book-rules**：W1 发现 `epic-book` 启动作业只读目录，配方要等第一次合成才在执行器上加载。这是 §2.3 之外的 A 类，按 RQ-091 纳入。
- **W2 的 `??=` 检测**：
  - 反射找 `Lazy<>` 与 `ConcurrentDictionary<,Lazy<>>` 实例字段；
  - 源码中凡是 `X ??=`，只在「同一文件里声明的某个类型有名为 X 的实例字段」时才算持有者；
  - 用 `is null` 写法的首次索引检测不到，由探针与 W1 的参与者集合覆盖。
- **持有者表 `ContentWarmupTests.Holders`**，每项是下面三种之一：
  - 自己是参与者；
  - 经某个参与者预热，或由已有的启动项预热；
  - 豁免并写明理由：35 个，其中 32 个「探针未在执行器上触达」，3 个不是内容缓存。
- **W1 除名字集合与条目数外，还逐字段核对持有者已加载**，并检查按职业、按族的键覆盖全部键。只看条目数的话，V9、V10 都抓不住。
- **开关的覆盖**（第二次停止后的裁定第 1～3 条）：
  - 进程内只有三个测试宿主会注册 `GameServerStartupService`：`CharacterizationHost`、`GmTestHost`、`ComponentLifecycleTests`。检索 `ConfigureGameServer`，测试中只命中这 3 个文件。
  - `GmTestHost` 与 `ComponentLifecycleTests` 默认关；
  - `CharacterizationHost` 的使用方全是要求保持开启的用例（表征测试 6 个文件与停机 S2），所以不加这个键，保持生产默认；
  - `Networking.Tests` 的 `TestInfrastructure`、HttpApi 管线测试、Application／Protocol 的测试组合都不注册 `GameServerStartupService`，预热本来就不会运行，所以没有加这个键：加了也不起作用。
- **对原禁令的重新评估**（裁定第 4 条）：
  - 开关只决定这次启动有没有提前加载；
  - **开关关闭时，执行器上的首次加载与 TASK-15 之前相同**，加载代码与缓存都没变；
  - 预热路径本身仍由 W1、表征测试、打包进程测试和 S2 在开启状态下覆盖。

### 保持开启预热的测试（裁定第 3 条）

- **W1 与 `ContentWarmupTests`**：S1 用 configuration 显式设为 true；F1 与开关测试直接调 `ContentWarmup`。
- **全部表征测试**，用的是 `CharacterizationHost`，没有设置这个键，即生产默认：
  - `SelectCharacterSuccessCharacterizationTests`、`SelectCharacterCompensationCharacterizationTests`、`PartyCharacterizationTests`、`PartyTowerCharacterizationTests`；
  - 结果 8 通过、2 跳过，金样本未改。
- **停机 S2**：`Networking.Tests/Integration/ShutdownPositionPersistenceTests`（`CharacterizationHost`）。
- **拉起真实打包进程的用例**，都用生产 appsettings：`EndToEndHostHarness` 及使用它的用例、`LauncherAuthenticationEndToEndTests`、`LauncherHttpHostStartupTests`、`ChannelHostStartupTests`、`HostShutdownOrderTests`、`AvatarCompoundTcpTests`。
- **joust 的 Warning**：正式全量与 Host.Tests 隔离运行中 `Content warmup failed` 都是 0。
  - 开着预热的进程内宿主（S1）用真实内容，没有这条 Warning；
  - 表征宿主与打包进程的日志不进测试输出，有没有这条 Warning，从日志里看不到。

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 警告、0 错误（`logs/task-15-build-test.txt` 开头）；做完变异后再次 `--no-incremental` 构建，同样 0／0。
2. **正式全量**（`logs/task-15-build-test.txt`，2026-10-22 19:36:54～19:39:46，运行前后 `yes` 进程都是 0）：
   - 总数 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 969 ＋ 173 ＋ 4502 ＋ 182 ＝ 8136 ＝ 8130 ＋ 6 － 0，只有 Host 增加（176 → 182）。
   - 通过 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 967 ＋ 171 ＋ 4484 ＋ 181 ＝ 8113；失败 3；跳过 2 ＋ 2 ＋ 16 ＝ 20；8113 ＋ 3 ＋ 20 ＝ 8136。
   - 3 条失败：2 条是那 2 条 `DimensionDungeonTests`；另 1 条是 `LauncherHttpHostStartupTests.GmComponentHasItsOwnListenerAndDoesNotExposeLauncherRoutes(launcher: False, game: True)`，`TaskCanceledException`，用时 15 s，等于它自己的预算。
     - 隔离 5 轮全部通过，每次约 3 s；
     - 复跑全量（scratchpad `t15/full-rerun.txt`）里它通过，用时 9 s。复跑中另有一条 `ShutdownOrderWireTests.Stopping_the_host…` 失败，是 `Connection refused`，输出里有 2 次 `Address already in use`，与 TASK-14 Remaining 第 2 条记录的端口争用形状、用例名都相同，与本单元无关。
     - 归因与处置见 `logs/task-15-isolated.txt`：打包进程保持生产默认、开着预热，同一次全量里 S1 的预热用了 14234 ms。属于预算问题，交主控方。
   - 表征测试 8 通过、2 跳过；挂起转储 0。
   - 日志信号：
     - 为 0：`direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`、`GameplayExecutorStoppedException`，以及 startup、timer、event、http 四类 `Gameplay …:… failed`；
     - `Gameplay gm:… failed` 2 条（auth/login、auth/logout）与 `HTTP API request failed` 2 条，和 TASK-14 相同，是注入的审计故障；
     - `Gameplay inbound:X failed` 3 条，用例故意注入，与 TASK-09～14 相同；
     - `the drain tears it down` 1 条，历次在 1～6 条之间；
     - `Content warmup failed` 0 条。
   - **耗时比对**（`logs/task-15-slow.txt`）：共同用例逐条耗时之和 2690.4 → 2723.7 s，**＋1.2%**，不超过 20%。
     - Host 954.5 → 1024.7 s，+7.3%；Networking 95.7 → 115.9 s，+21.1%（单个项目，基数小）；其余项目在 −5.0% 到 +12.2% 之间。
     - 多出 ≥ 9 s 的有 7 条：S1 多 18 s，是满载下预热 14.2 s；Launcher 那条超时；其余 5 条都是满载波动形状，没有 30 s 停顿。
   - **跑了预热分支的宿主启动次数**：在全量与 Host 隔离运行的日志里都只有 1 次（S1），另外 159 次记了「Content warmup skipped by configuration」。表征宿主与打包进程的日志不进测试输出，它们的次数**日志不足以确定**。按代码检索，上界是：表征用例 10 条（其中 2 条跳过）加 S2 1 条，每条至多起一个 `CharacterizationHost`；打包进程用例每条起 1～2 个进程。
   - **RSS**（单位 KiB，采样方式与 TASK-14 相同：scratchpad `t14/sampler2.sh`，2 s 一次，读 Host.Tests 的 testhost）：
     - Host.Tests 单独全量的峰值 13454768 KiB；
     - TASK-14 同一脚本测得：工作区各轮 11440064～15931440 KiB，基准导出 13484032 KiB；
     - 不超过 15931440 KiB，不触发停止条件。
   - **开关落地前的那次全量**（`logs/task-15-build-test-r1.txt`、`task-15-slow-r1.txt`）作为对照保留：耗时 +33.3%，160 次宿主启动跑了预热，每次预热的 p50 是 4743 ms，正是那次触发了停止条件。
3. **隔离复跑**（`logs/task-15-isolated.txt`）：
   - Host.Tests：182 条全部通过，1.75 min；
   - Networking.Tests（`--blame`）：171 通过、0 失败、2 跳过，没有崩溃；
   - Launcher 那条用例：5 轮全部通过。
4. **新增测试 ×10**（`logs/task-15-new-x10.txt`）：每轮 7 条（6 个新方法 ＋ S1），隔离 10 轮，10 × 7 ＝ 70 次通过；正式全量负载下 7 条全部通过。
5. **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本未改，`git status` 中没有这个目录的改动。
6. **变异核对**（`logs/task-15-mutations.txt`；每项改完逐字节还原，快照前后 sha1 相同，最后 `--no-incremental` 重建，0／0）：
   - V9 删掉成就这一项注册 → W1 红；
   - V10 技能只预热第一个职业 → W1 红；
   - V11 在表外类型上加一个 `Lazy<int>` → W2 红；
   - V12 去掉逐项的异常处理 → F1 红；
   - V13 开关缺省改为 false → `The_switch_defaults…` 红；
   - V14 忽略开关 → `Switched_off…` 红。
   - 开关落地后又完整做了一轮 V9～V14，每项都恰好一条用例变红。
7. **测试改动核对**（`logs/task-15-diff-check.txt`，`t07/diffcheck7.py bfbcf012 --lines`）：
   - 改动的 3 个测试文件，测试方法集合 8 ＝ 8，断言行多重集合 36 ＝ 36；
   - 白名单外的片段只有 4 处：S1 的 configuration 参数、S1 末尾的 W1 调用、两个宿主的开关键与注释。
   - 测试替身：没有补任何成员。
8. **生产改动的边界**（`logs/task-15-boundary.txt`）：
   - 原有 Abstractions 接口新增成员：0；
   - `IGameplayQueue` 37 行、`SuppressFlow|RunDetached` 14 行，与基准逐行相同；
   - `ContentWarmup.cs` 中 `IGameplayQueue|DomainTransaction|Gate` 的命中为 0。
9. **探针**（`logs/task-15-firstload-r1.txt`、`-r2.txt`）：
   - 第 2 轮清单内的项为 0；
   - 残留按类别逐项列出：D 类 5167 行、C 类 24 行、startup 2 行，5167 ＋ 24 ＋ 2 ＝ 5193；
   - `startup:` 类型下只有 epic-book 作业自己的加载；
   - 还原证明三项见 r2 日志开头：补丁只打在 scratchpad 副本上；快照前后相同；在 `Server/src` 检索探针名为 0。
10. **冷启动测量**（`verification/measurement/task-15-coldstart.md`，合成负载，P0／P1／P2 各两次）：
    - P1 的首次选角 101.7／104.3 ms，仍高于 100 ms；
    - 来源：P2（C 类全部预热）仍是 104.4／104.7 ms，探针第 2 轮里选角 CMD 下没有 A、B 类首次加载，之后的选角只要约 3 ms。推断剩余部分是 JIT 与 SQLite 冷页，交 M-1。
11. **对账行**（AC-006）：附录 B「启动预热」行，按「清单项 → 拥有者 → 入口 → 证据」写，另写了不预热的项及理由、开关、TASK-12 Result 第 5 条的关闭依据（第 2 轮里 `event:` 下已经没有成就、称号目录的加载）、交 M-1 的部分。

### Remaining

1. **打包进程用例的固定预算**：这类用例保持生产默认、开着预热，满载下有撞上 15 s 预算的风险（正式全量出现 1 次，隔离与复跑都通过）。请主控方裁定：
   - 放宽这些用例的预算；
   - 或接受「满载假红、隔离复跑」的处理；
   - 或由 TASK-16 的重型宿主检查一并评估。
2. **交 M-1**：
   - 首次选角超出 100 ms 的约 4 ms，以及剩下约 100 ms 的来源（推断是 JIT 与 SQLite 冷页）；
   - C 类的单键首次加载：单键 p99 不超过 0.48 ms，max 不超过 33.2 ms；
   - D 类：**周签到是性能关注点**，几乎每条玩法 CMD 都读档案，单个作业最多读 74 次；另两项是超额技能契约、每日补给规则（每次选角都重读 stackable.lst 与装备路径表）。
3. **档案中的既有坏项** `map/grandine/43408.map`（`[type] [noamal]`）：请求进对应副本时会失败；P2 的房间掉落那一族也因此没测到。
4. **32 个「探针未在执行器上触达」的豁免持有者**（装备强化、NPC 商店、邮件物品等）：负载之外的 CMD 第一次用到它们时，首次加载仍发生在执行器上。按第 2 版修订第 2 条的范围不预热，列给 M-1。
5. **预热最贵的一项是 drop-rules**：遍历装备族约 11.8 万个档案文件。单个 Release Host 上约 0.8 s，测试进程满载下 8～14 s。
6. **未提交**：没有 commit，没有 stash。

## Progress（执行者，第 3 轮，2026-10-22：按审查 B2 补入后，启动 RSS 预算超标，停下回报）

**已完成**（未提交）：

**B1**：W1 的检查顺序改为下面四步。
1. 参与者名集合与清单相等；
2. 断言 S1 宿主日志恰有一条「Content warmup finished: N items, 0 failed」，并且没有 failed 或 skipped 行；
3. 在调用任何 `WarmAsync` 之前核对持有者字段已加载；
4. 最后每项再调用一次，返回大于 0。

S1 改为传入 `host.Logs.Messages`。新增变异 V15「删掉 `StartingAsync` 中的预热调用」，W1 变红，红在日志断言 `Assert.Single`。V9～V15 全部按预期变红；逐字节还原后快照前后都是 sha1 `94d37c00…`，`--no-incremental` 重建 0 警告、0 错误。原始输出在 scratchpad `t15/mutations3.txt`。

**非阻断项**：
- 替身占位时记 Warning「Content warmup skipped」，并新增用例 `A_participant_replaced_by_a_double_is_reported_and_not_warmed`；
- 汇总行改为「N items, F failed」，N 包含失败项与被跳过的项；
- F1 的断言随之更新。

**B2**：做了静态可达性判定，40 个整索引持有者实现了契约，参与者从 31 个增加到 71 个。补入的写法是把 `F ??= …` 原样移进一个私有方法（引用类型字段带 `MemberNotNull`），原调用处改为调用这个方法，预热与请求走同一处加载代码。脚本在 scratchpad `t15/round3_patch.py`。
- 附录 B「启动预热」行按第 3 轮重写：证据栏、豁免、交 M-1、W2 的识别边界。
- Host.Tests 中 S1 与 `ContentWarmupTests` 共 8 条全部通过，W1 在 71 项上为绿。

**判定表**：

| 持有者 | 加载内容 | 可达入口举例（内容源 ← 规则服务 ← 领域服务 ← 执行器上的 CMD） | 处置 |
|---|---|---|---|
| PvfAvatarCompoundDefinitionSource | compoundavatar.etc 职业表与各职业定义 | AvatarCompoundRuleService ← AvatarCompoundDomainService（时装合成） | 纳入 avatar-compound |
| PvfCargoItemDefinitionSource | equipment.lst ＋ stackable.lst 路径表 | CargoTransferDomainService ← MoveItemSpaceSlice | 纳入 cargo-item-index |
| PvfCeraShopDefinitionSource | 商城目录 ＋ abilitydatas.dat | CeraShopRuleService ← CeraShopDomainService；JoustRuleService | 纳入 cera-shop |
| PvfCreatureDefinitionSource | 宠物目录 | ItemDeliveryRuleService、QuestAvailabilityRuleService、WorldContentEntryRuleService | 纳入 creatures |
| PvfCreatureEquipmentLifetimeSource | 宠物装备期限 | CharacterEntryPreparationRuleService（选角）、CreatureRuleService | 纳入 creature-equipment-lifetimes |
| PvfCreatureRuleSource | 宠物规则目录 | CreatureRuleService ← CreatureRuntimeDomainService | 纳入 creature-rules |
| PvfCycleDungeonDefinitionSource | 循环副本区域 | CycleDungeonRuleService ← CycleDungeonEntryDomainService | 纳入 cycle-dungeons |
| PvfEquipmentDisjointDefinitionSource | 两份路径表、出售系数、便携分解机等级、系统表 | EquipmentDisjointRuleService ← EquipmentEffectPlanRuleService | 纳入 equipment-disjoint |
| PvfEquipmentEnchantContentSource | 两份路径表 | EquipmentEnchantRuleService ← EquipmentEffectPlanRuleService | 纳入 equipment-enchant-index |
| PvfEquipmentForgingContentSource | 锻造表 | EquipmentUpgradeRuleService、ItemEquipmentRuleService | 纳入 equipment-forging |
| PvfEquipmentMagicSealContentSource | 两份路径表 ＋ randomoption.lst | EquipmentMagicSealRuleService ← EquipmentEffectPlanRuleService | 纳入 equipment-magic-seal-index |
| PvfEquipmentMagicSealGradeDefinitionSource | 封印等级表 | EquipmentMagicSealGradeRuleService ← EquipmentQualityDomainService | 纳入 equipment-magic-seal-grades |
| PvfEquipmentOutworldContentSource | 异界表 | EquipmentUpgradeRuleService | 纳入 equipment-outworld |
| PvfEquipmentQualityDefinitionSource | stackable 路径表 | EquipmentQualityRuleService ← EquipmentQualityDomainService | 纳入 equipment-quality-index |
| PvfEquipmentReinforcementDefinitionSource | 强化表、增幅表、stackable 路径表 | EquipmentUpgradeRuleService ← EquipmentReinforcementDomainService | 纳入 equipment-reinforcement |
| PvfEquipmentRepairDefinitionSource | equipment 路径表、修理费率 | EquipmentRepairRuleService ← EquipmentRepairDomainService | 纳入 equipment-repair |
| PvfEquipmentRequirementDefinitionSource | equipment 路径表 | EquipmentWearDomainService ← ItemMovementDomainService | 纳入 equipment-requirement-index |
| PvfEquipmentResealContentSource（W2 认不出） | 重封表 | EquipmentResealRuleService ← EquipmentResealDomainService | 纳入 equipment-reseal |
| PvfEquipmentSlotDefinitionSource（W2 认不出） | equipment 路径表 | ItemPlacementRuleService ← InventoryEffectHandlers 等 | 纳入 equipment-slot-index |
| PvfExperienceItemDefinitionSource | stackable 路径表 | ExperienceItemRuleService ← ExperienceItemDomainService | 纳入 experience-item-index |
| PvfHellDungeonDefinitionSource | 地狱区域；dungeon、map、aicharacter 列表；队伍定义 | HellDungeonEntryRuleService、HellRewardRuleService | 纳入 hell-dungeons |
| PvfIllusionTowerContentSource（W2 认不出） | 11000、11001 定义 | IllusionTowerRuleService ← IllusionTowerDomainService | 纳入 illusion-towers |
| PvfInventoryExpansionDefinitionSource（W2 认不出） | 背包与仓库扩展表 | InventoryExpansionRuleService、CeraShopRuleService | 纳入 inventory-expansion |
| PvfInventorySortKeySource | 两份路径表 | InventorySortDomainService ← SortItemCommand | 纳入 inventory-sort-index |
| PvfItemCatalogSource（W2 认不出） | 两份路径表 | AchievementRuleService、WeeklyAttendanceRuleService、ItemCatalogRuleService | 纳入 item-catalog-index；全量数组 `searchable` 按 C 类处置 |
| PvfItemSaleDefinitionSource | 两份路径表 | ItemSaleRuleService ← ItemSaleDomainService | 纳入 item-sale-index |
| PvfItemSocketDefinitionSource | 两份路径表 ＋ 装备类型表 | ItemSocketRuleService、ItemEquipmentRuleService | 纳入 item-socket-index |
| PvfItemUsageRuleSource | stackable 路径表 | ItemUsageDomainService、SpecialConsumableEffectPlanRuleService | 纳入 item-usage-index |
| PvfItemUseRuleDefinitionSource | stackable 表 | PlayingCharacter ← CharacterDomainService（道具使用） | 纳入 item-use-index |
| PvfLuckyStarRentalContentSource | 目录 ＋ 两份路径表 | LuckyStarRules ← AccountLuckyStarDomainService | 纳入 lucky-star-rental |
| PvfMailItemDefinitionSource | 两份路径表 | MailItemRuleService ← MailItemTransfers | 纳入 mail-item-index |
| PvfMirrorDungeonDefinitionSource | 镜像副本目录 | MirrorDungeonRuleService ← MirrorDungeonEntryDomainService | 纳入 mirror-dungeons |
| PvfNpcShopDefinitionSource | npc、itemshop、stackable、equipment 列表 | NpcShopRuleService ← NpcShopDomainService；HousingOperations | 纳入 npc-shop-index |
| PvfQuestParameterSource | 任务参数 | QuestContentRuleService | 纳入 quest-parameters |
| PvfRecipeContentSource | stackable 路径表 | RecipeCraftDomainService ← CompoundItemSlice | 纳入 recipe-index |
| PvfStackableActionDefinitionSource | stackable 路径表 | LevelUpTicketRuleService、QuestClearCouponRuleService 等 | 纳入 stackable-action-index |
| PvfTeleportConsumableDefinitionSource | stackable 路径表 | TeleportConsumableRuleService ← TeleportConsumableDomainService | 纳入 teleport-consumable-index |
| PvfTutorialRewardDefinitionSource | 教程奖励表 | TutorialRewardRuleService ← TutorialRewardDomainService | 纳入 tutorial-rewards |
| PvfDropRateTableSource（W2 认不出，有界键） | 掉落率表：怪物、怪物额外、可破坏物；金币表：怪物、可破坏物、结算 | ObjectDropRuleService、ClearRewardRuleService | 纳入 drop-rate-tables |
| PvfClearRewardRateSource（W2 认不出，有界键） | 结算掉率表 | ClearRewardRuleService、DespairTowerRuleService 等 | 纳入 clear-reward-rates |
| PvfItemTooltipSource | 路径表 ＋ 职业名表 | 只有 GmItemEndpoints 的 tooltip 路由，属于附录 B 中的执行器外路由 | 豁免 |
| PvfArchiveIdentity（W2 认不出） | 档案哈希 | GM server-info 路由，执行器外 | 豁免（不是内容索引） |

**汇总**：
- 原豁免的 32 个 `??=` 持有者：纳入 31 个，豁免 1 个（`PvfItemTooltipSource`），31 ＋ 1 ＝ 32；
- W2 认不出、第 3 轮检索到的：纳入 9 个，豁免 1 个（`PvfArchiveIdentity`）；
- 本轮纳入合计 31 ＋ 9 ＝ 40，参与者 31 ＋ 40 ＝ 71。
- 其余带缓存的内容源只有按键缓存（C 类），或已由已有启动项、已有参与者覆盖。

**停止原因**：重测 P1 两次（`verification/measurement/task-15-coldstart.md` 尚未更新，原始记录在 scratchpad `t15/runs/p1v3-r1`、`-r2`）。

| 量 | P0 | 第 3 轮 P1 | 增量 | 预算 |
|---|---:|---:|---:|---:|
| Host 就绪 | 1915／1432 ms | 6445／3936 ms | ＋4530／＋2504 ms | ＋10 s 以内 ✓ |
| 就绪时 RSS | 698096／640176 KiB | 1231984／1223968 KiB | **＋76.5%／＋91.2%** | ＋25% 以内 ✗ |
| 汇总行 | — | 71 items, 0 failed，2487／2541 ms | | |

- 首次选角 101.1／104.2 ms，首次进本 56.8／57.6 ms，(f) 138.2／140.5 ms，与第 2 轮的 P1 相同。
- 内存来源（`logs/task-15-warmup-memory.txt`）：
  - 托管堆在预热中从 243153 KiB 涨到 811037 KiB；
  - 本轮 40 项的增量之和 365935 KiB；
  - 其中 13 项是 equipment.lst／stackable.lst 的「id → 路径」字典，每个内容源各持一份，每份 33～49 MiB。
- 按停止条件停下，没有跑正式全量。新增测试 ×10、隔离复跑、全量都还没有在第 3 轮的代码上做。

**当前 owned diff**：第 2 轮的全部改动，加上：
- 40 个内容源；
- `InfrastructureModule` 的注册；
- `ContentWarmup` 与 `IContentWarmup` 的小改；
- `ContentWarmupTests`、S1；
- 附录 B。

**下一步**（等裁定）：
- 甲：这 13 个「id → 路径」字典共用一份路径索引。这是结构改动，会改变这些内容源的加载与缓存实现，超出「不改加载逻辑」的边界，需要授权。
- 乙：把这 13 个路径字典按 C 类（按物品）处置，只预热其余 27 项。
  - 单项增量里有负数（强制回收所致），能省下多少内存不足以确定，能否回到 ＋25% 以内要重测。
- 丙：放宽 RSS 预算。

## 主控方裁定（第 3 轮停止之后：就绪 RSS 比基线多 76.5% 和 91.2%）

**选丙：放宽「就绪 RSS ＋25%」这一项启动预算。其余停止条件不变。**

1. **理由**：
   - 预热只是把加载提前，不增加稳态内存。占内存的主体是 13 个内容源各自持有一份 equipment.lst / stackable.lst 的「id → 路径」字典，内容相同，每份 33～49 MiB。今天这些字典在相应功能首次被使用时也会加载，生产跑一段时间之后，RSS 会到同一个水平。
   - ＋25% 的预算原本是为了约束启动阶段，以及测试端的内存压力。测试宿主现在默认关闭预热，测试端的内存另有停止条件约束。
2. **新的记录要求**：在 Result 和 `measurement/task-15-coldstart.md` 中写出以下几项：
   - 第 3 轮 P1 的就绪 RSS，以及与 P0 的增量；
   - 托管堆的增长：243153 → 811037 KiB；
   - 13 份重复字典的明细。
   
   还要写明一个推断：生产稳态下即使没有预热，RSS 也会达到同一量级。如果这一推断有测量依据（例如用 P0 跑构成 A 之后的 RSS），一并附上；没有就写「推断，未测」。
3. **后续事项**（不在本任务范围）：「13 个内容源重复持有同一份 equipment / stackable 路径字典，约 500 MB」是既有的内存浪费。写进 Remaining，标为「交主控方立项」；TASK-16 收口时，汇总到后续事项清单。本单元不改内容源的加载与缓存实现（方案甲不采纳）。
4. **继续**：在第 3 轮的代码上完成以下检查：
   - 正式全量；
   - 两项测试端停止条件：共同用例逐条耗时之和对 TASK-14 不超过 ＋20%；Host.Tests 的 testhost RSS 峰值以 KiB 计、同一采样方式，不超过 TASK-14 的上限 15931440 KiB；
   - 新增测试 ×10；
   - 隔离复跑；
   - 更新冷启动记录；
   - 写 Result（标第 3 轮）。
   
   开启预热的测试宿主（表征测试、S1、打包进程、S2）现在每个约 1.2 GB。测试端任一停止条件超标时，停下回报。

## Progress（执行者，第 3 轮「第 3 轮停止之后」的裁定执行中，2026-10-22：正式全量耗时 +24.4%，停下回报）

**已完成**（未提交）：
- 冷启动记录：在 `verification/measurement/task-15-coldstart.md` 末尾追加了「第 3 轮 P1」一节，按裁定第 2 条写入：
  - 就绪 RSS 及与 P0 的增量；
  - 托管堆 243153 → 811037 KiB；
  - 13 份字典的明细及加法；
  - 「推断，未测」：已有的 P0 数据不足以支持「稳态下同一量级」，构成 A 的那次没有采样 RSS。
- 变异：第 3 轮 V9～V15 的原始输出已追加到 `logs/task-15-mutations.txt`。
- 正式全量（`logs/task-15-build-test.txt`，2026-10-22 20:31:34～20:35:06）：
  - 总数 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 969 ＋ 173 ＋ 4502 ＋ 183 ＝ 8137 ＝ 8130 ＋ 7（Host 176 → 183）；
  - 通过 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 967 ＋ 171 ＋ 4484 ＋ 183 ＝ 8115，失败 2（就是那 2 条 `DimensionDungeonTests`），跳过 20；8115 ＋ 2 ＋ 20 ＝ 8137；
  - 挂起转储 0。
- Host.Tests 与 Networking.Tests 隔离复跑（`logs/task-15-isolated.txt`）。

**两项测试端停止条件**：

| 条件 | 实测 | 判定 |
|---|---|---|
| 耗时：共同用例逐条耗时之和对 TASK-14 ≤ ＋20% | 2682.7 → 3338.4 s，**＋24.4%** | **超标，停下** |
| RSS：Host.Tests 单独跑，sampler2，2 s 一次 | 峰值 **14247072 KiB**，TASK-14 上限 15931440 KiB（第 2 轮为 13454768 KiB） | 未超 |

**耗时超标的形状**（`logs/task-15-slow.txt`）：
- 全量对第 2 轮（同一台机器）也慢了 23.1%，而且变慢不限于 Host：Application ＋23.5%、Networking ＋40.0%、Protocol ＋16.6%、Persistence ＋11.9%。这几个项目的测试代码与走的路径在第 3 轮都没有变。
- swap 已用：全量开始时 7215 MB，最高涨到 11999 MB。
- S1 的预热在满载下用了 50.4 s，单独跑 Host.Tests 时是 11.2 s。
- 推断，未测：开着预热的宿主（S1、表征测试、S2、打包进程）每个约 1.2 GB，加剧了换页，于是所有项目都变慢。按裁定的排查顺序：
  - 本单元路径：预热的内存增量，约 0.5 GB／宿主；
  - 换页：swap 增长约 4.8 GB；
  - 线程栈：没有工具可取。
- 机器起点也比第 2 轮差：第 2 轮开始时 swap 是 11627／13312 MB，本轮是 7215／8192 MB，总量刚被系统收缩过，可用的 swap 余量更小。两次的起点不同，环境因素与本单元因素没有分开测。

**未做**：新增测试 ×10（第 3 轮代码上只做了变异时的定向运行，8 条每轮都通过）、测试改动核对与边界 grep 的第 3 轮更新、Result（第 3 轮）。

**当前 owned diff**：同第 3 轮 Progress，另加 `task-15-coldstart.md` 的第 3 轮一节，以及 `logs/task-15-slow.txt`、`task-15-isolated.txt` 的第 3 轮版本。第 2 轮的这两份已改名为 `-r2` 保留。

**下一步**（等裁定）：
- 若接受（例如认为超标主要来自环境）：复跑一次正式全量取第二个样本，然后补做 ×10、核对与 Result。
- 若要降低测试端代价：可选范围是开着预热的那几类测试宿主（表征、S2、打包进程），或者减少 13 份重复字典。后者已被裁定为后续事项，本单元不做。

## Result（第 3 轮）

本节取代前面 `## Result` 中与第 3 轮冲突的内容。前面那节是第 2 轮的结果，保留为当时的记录。

### Outcome

- **范围**：71 个参与者，统一实现 `IContentWarmup`。由 `ContentWarmup` 在 `StartingAsync` 中、执行器外、开始监听之前逐项加载。
  - 31 个来自 §2.3 与探针第 1 轮。
  - 40 个是审查 B2 之后按静态可达性补入的整索引。
  - 现在所有执行器能触达的整索引（A 类）与有界按键缓存（B 类）都在启动时加载。没有纳入的只剩三类：C 类按键缓存、D 类不缓存的读档、4 个有理由的豁免。
- **W1**（审查 B1 之后）：先断言 S1 宿主日志恰有一条「Content warmup finished: 71 items, 0 failed」，再在调用任何 `WarmAsync` 之前核对字段已加载。删掉启动调用（V15）会让它变红。
- **冷启动**（合成负载）：首次选角 493.3／534.7 → 101.1／104.2 ms，首次进本 76.0／839.0 → 56.8／57.6 ms，(f) 527.6／860.5 → 138.2／140.5 ms。
- **启动代价**：就绪 RSS 比 P0 多 ＋76.5%／＋91.2%。主控方已放宽原 ＋25% 预算（第 3 轮停止之后的裁定）。
- **测试端**：用户已撤销耗时与 RSS 两项停止条件。两次正式全量都只有那 2 条 `DimensionDungeonTests` 失败；耗时与 RSS 照实记录在下面，作为信息。
- RQ-091 的达标判定归 M-1。

### Changed

**生产**（`logs/task-15-boundary.txt`）：`Server/src` 73 个已跟踪文件 ＋ 3 个新文件。
- **契约**：`Abstractions/Content/IContentWarmup.cs`。
  - `IContentWarmup`；
  - `ContentWarmupRegistration.AddContentWarmup<TService>()`：注册的是同一个单例；被替身占位时登记一个占位项；
  - `IsReplaced(participant)`。
- **`GameServer/ContentWarmup.cs`**：
  - 开关 `ContentWarmup:Enabled`：缺省 true，非法值抛异常；
  - 逐项捕获，失败记 Warning 后继续，取消照常抛出；
  - 替身占位记 Warning「Content warmup skipped」；
  - 汇总行「Content warmup finished: N items, F failed, in … ms」，N 包含失败项与被跳过的项。
- **`GameServerStartupService`**：档案分支末尾调用一次。csproj 加了 `InternalsVisibleTo Dfo.Server.Host.Tests`。
- **参与者**：
  - Infrastructure 62 个内容源，注册集中在 `InfrastructureModule.AddContentWarmups`；
  - Application 9 个：会员服务、任务助手人偶、经验、Joust、比武、史诗书、掉落规则，新类 `CharacterSkillRuleWarmup`，以及 `ApplicationModule` 中的注册；
  - 名单的唯一出处是 `ContentWarmupTests.Participants`。
- **第 3 轮 40 个内容源的写法**：原来的 `F ??= 表达式;` 原样移进私有方法 `<F>Index(...)`（引用类型字段带 `MemberNotNull`），原调用处改为调用它。加载逻辑与缓存实现不变。

**测试**：
- `ContentWarmupTests.cs`（新增），7 个方法：
  1. `W2_every_lazy_or_first_use_index_holder_is_warmed_or_exempt_with_a_reason`
  2. `W2_rejects_an_unlisted_lazy_field_and_an_unlisted_first_use_index`
  3. `F1_a_failing_item_is_logged_and_the_rest_still_warm`
  4. `F1_cancellation_is_not_swallowed`
  5. `Switched_off_the_warmup_resolves_and_calls_no_participant`
  6. `The_switch_defaults_to_on_and_only_an_explicit_false_turns_it_off`
  7. `A_participant_replaced_by_a_double_is_reported_and_not_warmed`

  W1 辅助是 `AssertWarmedAsync(services, hostLog)`。N ＝ 2 ＋ 2 ＋ 2 ＋ 1 ＝ 7，Host 176 → 183。
- **S1**：原断言 → 原断言 ＋ 末尾一行 `await ContentWarmupTests.AssertWarmedAsync(host.Services, host.Logs.Messages);`。宿主调用加 `configuration: { ContentWarmup:Enabled = true }`。
- `GmTestHost`、`ComponentLifecycleTests` 各加 `["ContentWarmup:Enabled"] = "false"` 与一行注释。
- 测试替身没有补任何成员，因为原有接口没有改。
- **`CharacterizationHost` 没有改**。第 3 轮执行中曾临时改为默认关闭，按用户新指示已原样撤回。

**文档与证据**：
- `spec.md` 附录 B「启动预热」行：第 3 轮重写，写了机制、开关、71 项、证据、不预热的项、W2 的识别边界、交 M-1。
- `verification/logs/`：
  - 正式全量：第 3 轮第一样本 `task-15-build-test.txt`，第二样本 `-r3b`；
  - 早先几轮（只作对照）：`-r1`、`-r2`、`-r3c-reverted-variant`；
  - 其余：slow 各轮、isolated 各轮、new-x10、mutations、diff-check、boundary、firstload-r1／r2、red、warmup-memory。
- `verification/measurement/task-15-coldstart.md`：P0、P1（第 2 轮与第 3 轮）、P2。
- `verification/probe-tools/firstload/`。

### Why（本地决定）

- **契约用异步形状、放在 `Abstractions/Content`**：理由见第 2 轮 Result。三个内容源原本就有私有 `Name(...)` 方法，它们的 `Name` 用显式接口成员实现。
- **整索引抽成方法，而不是在预热里再写一遍表达式**：如果预热用的是一份旧表达式，它先填好字段，请求那边的 `??=` 就不会再执行，两处会悄悄分叉。所以每个事实只保留一处。
- **可达性判定**：
  - 被纳入的 40 个，每个都由 Application 的规则服务或领域服务使用，往上由执行器上的 CMD、事件或定时作业调用；
  - 豁免的：`PvfItemTooltipSource` 只被 GM tooltip 路由读，那条路由在执行器外；`PvfArchiveIdentity` 是 GM server-info 路由用的档案哈希，不是内容索引；
  - 逐项表格见第 3 轮 Progress；
  - 物品目录给 GM 搜索的全量数组 `searchable` 是物品目录族的全量物化，按 C 类处置。
- **W1 的检查顺序**：先看日志，再看字段，最后调用参与者。这样它证明的是「宿主就绪时已经加载」，而不是「`WarmAsync` 能加载」。
- **开关的覆盖范围**：
  - 只在进程内的 `GmTestHost`、`ComponentLifecycleTests` 关闭；
  - S1（W1）、表征测试、停机 S2、打包进程测试都保持开启；
  - 开关关闭时，执行器上的首次加载与 TASK-15 之前相同。

### Checks

1. **构建**：`--no-incremental` 构建，0 警告、0 错误（两次全量日志开头都有）。
2. **正式全量**（用户已撤销测试端停止条件，数字只作信息）：

| 样本 | 总数 | 通过 | 失败 | 跳过 | 共同用例耗时对 TASK-14 | 对第 2 轮 |
|---|---:|---:|---:|---:|---|---|
| 第一样本 `task-15-build-test.txt`（20:31:34～20:35:06，开始时 swap 已用 7215／8192 MB） | 8137 | 8115 | 2 | 20 | 2682.7 → 3338.4 s，＋24.4% | ＋23.1% |
| 第二样本 `task-15-build-test-r3b.txt`（20:39:53～20:43:36，开始时 swap 已用 8827／10240 MB，空闲页 866994 × 16 KiB；已先停构建服务，确认没有残留进程） | 8137 | 8115 | 2 | 20 | 2690.8 → 3399.7 s，＋26.3% | ＋24.8% |

   - **计数加法**（两份相同）：
     - 总数 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 969 ＋ 173 ＋ 4502 ＋ 183 ＝ 8137 ＝ 8130 ＋ 7，只有 Host 增加，176 → 183；
     - 通过 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 967 ＋ 171 ＋ 4484 ＋ 183 ＝ 8115；
     - 8115 ＋ 2 ＋ 20 ＝ 8137；
     - 失败的就是那 2 条 `DimensionDungeonTests`；
     - 表征测试 8 通过、2 跳过；
     - 挂起转储 0。
   - **日志信号**（第二样本）：
     - `direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`、`GameplayExecutorStoppedException`，以及 startup、timer、event、http 四类 `Gameplay …:… failed` 都是 0；
     - `Gameplay gm:… failed` 2 条（auth/login、auth/logout），与 `HTTP API request failed` 2 条成对，同 TASK-14；
     - `Gameplay inbound:X failed` 3 条，用例故意注入；
     - `the drain tears it down` 2 条，历次在 1～6 条之间；
     - `Content warmup failed` 0 条，`Content warmup skipped:` 0 条。
   - **耗时的形状**：两个样本里，Host 以外的项目（Application、Networking、Protocol、Persistence）同样变慢，期间 swap 涨到最高 16968 MB。
     - 开着预热的进程内宿主只有 S1，它的预热在满载下用了 50～54 s，其中 drop-rules 23～30 s；单个 Release Host 上约 2.5 s。
     - 推断，未测：开着预热的宿主每个约 1.2 GB，加剧了换页，于是所有项目都变慢。
     - 参考：曾临时把表征宿主改为关闭预热，那次全量对 TASK-14 为 ＋8.2%（已撤回）。
   - **跑了预热分支的宿主启动次数**：进程内只有 S1 这 1 次，另 159 次记了 skipped。表征、S2 与打包进程的日志不进测试输出，次数**日志不足以确定**。按代码检索，上界是：表征 10 条（2 条跳过）＋ S2 1 条，每条至多起一个宿主；打包进程用例每条起 1～2 个进程。
3. **隔离复跑**（`logs/task-15-isolated.txt`，第 3 轮代码）：
   - Host.Tests：183 条全部通过，1.96 min；
   - **testhost RSS 峰值 14247072 KiB**，用 TASK-14 的 sampler2.sh，2 s 一次，单位 KiB。TASK-14 同口径上限 15931440 KiB，第 2 轮 13454768 KiB。只作信息。
   - Networking.Tests（`--blame`）：171 通过、0 失败、2 跳过，没有崩溃。
   - LauncherHttpHostStartupTests 在第 2 轮全量中出现过一次满载超时，第 3 轮的两个样本里都通过。
4. **新增测试 ×10**（`logs/task-15-new-x10.txt`）：每轮 8 条（7 个方法 ＋ S1），隔离 10 轮，10 × 8 ＝ 80 次通过；第二样本全量负载下这 8 条也都通过。
5. **表征测试**：8 通过、2 跳过。`Characterization/` 目录与金样本未改，`git status` 中没有这个目录。
6. **变异**（`logs/task-15-mutations.txt` 第 3 轮段；每项逐字节还原，快照前后 sha1 `94d37c00…` 相同，`--no-incremental` 重建 0／0）：

| 变异 | 改了什么 | 变红的用例 |
|---|---|---|
| V9 | 删掉成就目录的注册 | W1：名字集合不等 |
| V10 | 技能规则只预热第一个职业 | W1：键不是超集 |
| V11 | 给表外类型加一个 `Lazy<int>` | W2 |
| V12 | 去掉逐项异常处理 | F1 |
| V13 | 开关缺省改为 false | 开关默认用例 |
| V14 | 忽略开关 | 开关关闭用例 |
| V15 | 删掉 `StartingAsync` 中的预热调用 | W1：日志断言 |

   每项都恰好一条用例变红。
7. **测试改动核对**（`logs/task-15-diff-check.txt`，`t07/diffcheck7.py bfbcf012 --lines`）：
   - 改动的 3 个测试文件，方法集合 8 ＝ 8，断言行多重集合 36 ＝ 36；
   - 白名单外的片段只有 4 处：S1 的 configuration 参数、S1 末尾的 W1 调用、两个宿主的开关键与注释。
8. **生产改动的边界**（`logs/task-15-boundary.txt`）：
   - 原有接口新增成员 0；
   - `IGameplayQueue` 37 行、`SuppressFlow|RunDetached` 14 行，与基准逐行相同；
   - `ContentWarmup.cs` 中门、事务、队列的命中为 0。
9. **探针**：第 2 轮（31 项时）清单内项为 0，残留只有 C 类、D 类与 epic-book 作业自身的加载（`logs/task-15-firstload-r2.txt`）。第 3 轮补入的 40 项不在合成负载的路径上，没有重跑探针。
10. **冷启动**（`verification/measurement/task-15-coldstart.md` 第 3 轮一节）：

| 量 | P0 | 第 3 轮 P1 | 增量 |
|---|---:|---:|---:|
| 就绪 | 1915／1432 ms | 6445／3936 ms | ＋4530／＋2504 ms |
| 就绪 RSS | 698096／640176 KiB | 1231984／1223968 KiB | ＋533888 KiB（＋76.5%）／＋583792 KiB（＋91.2%） |

    - 托管堆在预热中从 243153 KiB 涨到 811037 KiB。
    - 13 份重复的「id → 路径」字典明细见测量记录，加起来 503025 KiB，只代表量级。
    - 「生产稳态下即使不预热也会到同一量级」：推断，未测。
11. **对账行**：附录 B「启动预热」行，第 3 轮重写。

### Remaining

1. **交主控方立项**：13 个内容源各自持有一份 equipment.lst／stackable.lst 的「id → 路径」字典，内容相同，合计约 500 MB（每份 33～49 MiB）。这是既有的内存浪费，预热把它提前到了启动时。本单元按裁定不改内容源的加载与缓存实现。TASK-16 收口时汇总进后续事项清单。
2. **测试端耗时代价，交后续性能优化**：
   - 开着预热的宿主（S1、表征测试、停机 S2、打包进程）每个约 1.2 GB；
   - 本机带 PVF 的全量比 TASK-14 慢 24%～26%；
   - 满载下 S1 的预热要 50～54 s；
   - 打包进程用例有撞上自身 15 s 预算的风险，第 2 轮出现过一次，隔离复跑通过。
   - TASK-16 的重型宿主检查应单列「带 PVF 的打包进程用例清单与预算」。
3. **交 M-1**：
   - 首次选角里约 100 ms 不是内容加载，推断为 JIT 与 SQLite 冷页；首次选角里 D 类读档那几条代码路径的首次 JIT 也算在内；
   - C 类的单键首次加载与物品目录的 `searchable`；
   - D 类：**周签到是性能关注点**；另两项是超额技能契约、每日补给规则；
   - 合成负载覆盖不到的 CMD 走到 C 类、D 类时，首次加载或读档仍发生在执行器上，M-1 的负载同样覆盖不到；
   - RQ-091 的达标判定。
4. **W2 的识别边界**：它认不出 `if (x is null)`、手写字典、`is {}` 这几种写法的首次索引。这几种写法的新增，要靠人工检索与参与者名单把关（附录 B 已写明）。
5. **档案中的既有坏项** `map/grandine/43408.map`（`[type] [noamal]`）。
6. **未提交**：没有 commit，没有 stash。

### 主控方补充（第二轮审查通过之后，提交之前）

- **记载更正**：
  - 参与者的构成应为 Infrastructure 63 ＋ Application 8，不是 Result 第 3 轮写的 62 ＋ 9。成就与称号簿两个类在同一个文件里；`ApplicationModule` 被误算成了一个参与者。总数 71 无误。
  - 附录 B「交 M-1」一栏里的首次选角，以第 3 轮测得的 101.1 / 104.2 ms 为准，结论不变。
- **补充 Remaining 第 1 条**：第 3 轮在 t＝60 s 时的 RSS 和全程峰值，比第 2 轮高约 0.8 GB，数字见冷启动表。为 13 份重复路径字典立项时，一并评估这部分。
- **用户指示**（2026-10-22）：不追求基线数字，性能可以接受即可。测试端耗时与 RSS 两项停止条件已经撤销，只作记录。单条指令执行时间以 ≤ 2 s 为接受线，另行写入 RQ-091 的修订。
