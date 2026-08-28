---
kind: rks-review
verdict: approved
---

# Change Review: 频道奖励按档案声明的副本归属发放

审查者以全新上下文独立进行，未采信执行者结论；执行者报告仅作为待验证的线索。

本件为**第二轮**：首轮在同一范围上得出 `approved` 并附 9 条非阻断观察，随后执行者按其中两条
补做了改动。本件覆写首轮结论，涵盖补做后的完整变更集。

## 审查范围与完整性

对照的完整变更集 = `2d121cf9` 之后工作树的全部未提交内容：

- `git diff` 的 12 个受改文件（其中 `.rks-agent/tasks/dungeon-run-experience/multi-spec-map.md` 属流程记录）；
- 5 个未跟踪产品/测试文件：`ChannelExperienceContent.cs`、`ChannelScriptExperienceSource.cs`、
  `ExperienceBonusServiceTests.cs`、`ChannelScriptExperienceSourceTests.cs`、
  `GrantKillExperienceCommandTests.cs`；
- 变更所依赖的档案 `Server/src/Dfo.Server.Host/Resources/Channel/channel_info.etc`（未改动）。

范围与 TaskContract 的 In/Out 一致：没有触碰通关结算的频道加成，没有读取 `[server]` 行里等级区间／人数上限，
没有让 `channelType` 参与经验计算（仅作合法性校验），没有按频道限制可进入副本。未发现越界改动，
也未发现契约要求而缺失的行为。

未提供对抗挑战集（sdd-lite，无 plan 件），故不含 `Challenge Dispositions`。

### 第二轮的增量

三处，且**生产代码一行未动**——三个产品文件与首轮备份逐字节 md5 相同
（`ChannelExperienceContent.cs` / `ChannelScriptExperienceSource.cs` / `ExperienceBonusService.cs`），
`git diff --stat` 在 `Server/src` 上与首轮一致。

1. `ExperienceBonusServiceTests` 的夹具由一个频道扩为两个（10→`[granfloris]`@0.05 含 144；
   11→`[sky_catle]`@0.20 含 157），`OneRuntimeSession` 按频道号参数化；新增
   `TheRateIsTheOneDeclaredForTheChannelTheSessionIsOn`（两例）、
   `AChannelEarnsNothingForAnotherChannelsDungeon`（两例）、
   `ACatalogueThatDeclaresNothingPaysNothing`。该类由 5 例增至 10 例。
2. RQ-007 的实机启动证据。
3. `Docs/HostStartup.md` 的事实性纠错。

首轮的另外若干条非阻断观察未在本子成果内处理，判断合理：
`count: 2` 与 appsettings 的耦合、类注释漂移属体量与收益不相称；
`Channels` 与 `GamePorts` 可能不一致是既有隐患，本成果只是多给了一次犯错机会，
扩到本成果里确实越界，写进后继指引是恰当的处置。

## 独立复核：解析器与真实档案是否一致

没有采信任何测试里的转写文本，而是通过反射直接调用
`ChannelScriptExperienceSource.Parse`，对**实际发货的** `channel_info.etc` 全量解析，
再与逐行手读的结果比对。

- 解析出 29 条频道行，频道号为
  `1,6,10..21,50..54,60..64,68,70,100,200,201`——与档案 `[server]` 段的行数与行号完全一致，
  开头那个孤立计数标量 `1` 没有被读成频道行（它与真实频道 1 同号，读错会污染 `[deathtower]` 那一行）。
- 字段下标 `0=频道号 / 1=显示名 / 2=频道类型 / 3=组名 / 4=经验百分比` 与档案逐行核对无误：
  10→`[granfloris]`5%、11→`[sky_catle]`5%、12→`[behemoth]`、13→`[Alfhlyra]`、14→`[stormpass]`、
  15→`[Antwer]`、16→`[seatrain]`、17→`[powerstation]`、18→`[timedoor]`、19→`[crack]`、
  20→`[noblsky]`、21→`[CastleofDead]`、100→`[sainthorn]`、200/201→`[raid]`，
  1→`[deathtower]`0%、6→`[trade]`0%、50..54/60..64/68/70→`[none]`0%（共 12 行）。
- `[dungeon]` 段 23 个组的副本号逐组核对无误，例如
  `[granfloris]={144,145,146,147,148,149,240,2009}`、`[sky_catle]={156..160,242,2010}`、
  `[stormpass]` 13 个、`[timedoor]` 15 个、`[sainthorn]` 14 个。
- 关键判据：`RateFor(10,144)=0.05`、`RateFor(11,144)=0`、`RateFor(11,157)=0.05`、
  `RateFor(10,156)=0`、`RateFor(1,11000)=0`、`RateFor(6,144)=0`、`RateFor(19,144)=0`、
  `RateFor(200,144)=0`、`RateFor(0,144)=0`。全档案里 `Covers(144)` 为真的频道有且仅有 10，
  这正是抓包那一局的推断所需，`TheShippedScriptDeclaresTheRateTheCaptureMeasured` 的
  `Assert.Single` 因而成立而非侥幸。
- CRLF、制表符、反引号包裹的空串、组名与显示名混排，分词器都处理正确；
  分词把带空格的引号串保持完整（实测显示名里塞进空格不会打乱后续下标）。

`DungeonId` 的编号空间也核对过：击杀路径传的是 `run.Layout.DungeonId`，
与档案给 144 定 `ExperienceWeight=1.45` 的是同一套编号，与 `[dungeon]` 段的号一致。
`ChannelId` 的来源核对过：`ChannelEntryPolicy` 按监听端口在 `Channel:Channels` 里查 `Id`，
经 `EntryPlacementRuleService` 落到 `ChannelEntryPolicyState.ChannelId`，
即 `result.World.Channel.ChannelId`——正是 `[server]` 行的频道号。

## 独立验证：负面用例是否真的被测住（变异实验）

本成果存在的理由是「一个能拟合四个样本的比率，说不清它何时适用」。
若测试只证明「组内会付」，就会在更高一层重犯同样的错。因此逐个植入变异、跑测试、再按 md5 还原：

| 变异 | 首轮 | 本轮 |
| --- | --- | --- |
| `Covers` 恒真（等价于 `RateFor` 忽略 `dungeonId`） | 红（4 例） | 红 |
| 服务把 `occasion.DungeonId` 换成常量 `144` | 红 | 红 |
| 百分比改读 `tokens[5]` | 红（8 例） | 红 |
| 组名改读 `tokens[2]` | 红（4 例） | 红 |
| 取消频道重复声明的作废（后者覆盖） | 红 | 红 |
| 取消组名重复声明的作废（先者胜出） | 红 | 红 |
| 去掉「计数标量不是频道行」的保护 | 红 | 红 |
| 去掉百分比上界 0–100 校验 | 红 | 红 |
| 服务把 `result.World.Channel.ChannelId` 换成常量 `10` | **绿（存活）** | **红**，恰为两条 `channelId: 11` 的用例 |
| 服务改读 `session.ListenerPort`（相邻字段的误取） | 红 | 红（3 例） |

首轮唯一存活的变异已被本轮补测封死：夹具让监听端口固定为 10010 而频道号在 10/11 间变化，
且两个频道的比率刻意不同（0.05 / 0.20），因此「从常量作答」「从唯一一行作答」
「误取相邻的监听端口」三种写法都无处藏身。负面用例是真的存在且真的在起作用。

## 契约与 Spec 逐条核对

- **RQ-001 / SC-1**：`ChannelExperienceCatalog.RateFor` 只在 `Covers` 为真时返回该行比率；
  四个抓包样本在 `GrantKillExperienceCommandTests` 端到端复现 65 / 75 / 132 / 153（RQ-010、AC-009）。
- **RQ-002 / SC-2 的反面**：同一副本 144 在频道 10 付 5%、在频道 11 不付；
  现在解析层与服务层各有**双向**两例（A 频道拿不到 B 频道副本的钱，反之亦然）。
- **RQ-003 / RQ-004 / RQ-005**：未声明频道、残缺行（<5 字段、类型非法、百分比 `abc` / `150` / `-5`）、
  频道或组名重复，全部不付，均有测试；且**双向**都作废——实测「好行在前坏行在后」与
  「坏行在前好行在后」都返回 0，三行两好一坏也返回 0，组名重复无论先后顺序都返回 0。
- **RQ-006**：`FromChannel` 以 `rate > 0` 为门；`GrantKillExperienceCommandHandler` 另有
  `Amount > 0` 过滤，零比率不产生条目，`AChannelThatAddsNothingProducesNoLine` 覆盖。
- **RQ-007 / SC-4**：结构上不可能因解析失败而起不来——没有任何 `IHostedService` 解析
  `IChannelExperienceSource`，`Load()` 只在首次击杀时惰性触发；`Parse` 对空串、无段落文本、
  只有 `[dungeon]`、只有 `[server]` 都返回空目录而非抛出。本轮另有实机证据（见下）。
  文件本身读不到仍由既有的 `ChannelScriptStartupService` 拒绝启动，未被改动。
- **RQ-008**：仍是 `ExperienceBonusKind.SeriaBlessing`，
  `MonsterDeathSlice` 的 `+0x1A` 映射与 `MonsterDeathCodecTests` 未改动，线级字节不变。
- **RQ-009 / SC-7**：`Experience:ChannelRate` 的注册已从 `ApplicationModule` 整体删除，
  `ExperienceChannelPolicy` 已删；`experience.json` 中该键已移除并留下指向档案的说明键，
  `ExperienceTuningSourceTests` 以「断言缺席」的方式钉住。既然没有任何代码再读这个键，
  「写什么都不改变它」成立。
- **RQ-011 / SC-7**：`appsettings.json` 同时发 10（`[granfloris]`，10010）与 11（`[sky_catle]`，10011），
  `Docs/HostStartup.md` 新增一节写明同一张 144 在两个频道上各看到什么。
- **SC-8**：本人在还原后的工作树上复跑：`dotnet build` 0 警告 0 错误；
  `dotnet test` **1539 通过 / 2 跳过**（均为无关的 PVF 缺档案跳过）**/ 0 失败**，
  `PVF_ARCHIVE_PATH` 指向 A21 档案。与执行者所报一致。

## 独立复核：RQ-007 的实机启动证据

亲自核验了执行者留下的证据，而非采信其描述：

- `scratchpad/junkchan/channel_info.etc` 实为 74 字节的三行英文散文，非空
  （因而通过既有的「空文件拒绝启动」校验），无 `[dungeon]`、无 `[server]`，
  正对应解析测试里「没有任何段落的胡话」那一类。
- `host.log` 显示 `ChannelScript=` 指向该文件，`ChannelPort=60384`、`GamePorts=60385,60386`
  均为随机端口（未占用发货的 10010/10011，不会撞开发机上的真服务器），
  且出现 `Application started`。启动确实成功。
- 复查该端口现无监听，进程已停。

执行者附带的观察经复核属实且重要：日志中**没有** `Channel experience: N channel(s)` 那一行
（实测 `grep -c` = 0），因为目录是惰性的、第一次击杀才读。
**这不是解析没跑的证据**，终验证据里必须这么写，否则会被误读成回归。

该证据覆盖 RQ-007 的「照常启动」一半；「击杀照常发经验、只是没有频道奖励」这一半
目前由 `ACatalogueThatDeclaresNothingPaysNothing` 与解析测试在单元层覆盖，
实机的那一次击杀留给 verification 阶段（EV-002 本就这么写）。

## 对首轮指定七点的判断（结论不变）

- **第3问**：**`ExperienceBonusService` 重构没有丢失可用行为。** 改动前：无会话时跳过特权项，
   但仍无条件付 5% 频道奖励；改动后：无会话直接返回空。这不是能力退化——新规则必须
   知道所连频道，而频道只存在于已发布会话里；且实际击杀路径
   （`MonsterDeathSlice`）在到达发放之前就已经要求 `session` 与已发布的 `result`
   （它要读技能镜像、要写回会话），所以该早返回在实机路径上不可达。
   HEAD 处不存在针对该服务的测试，故无既有测试被削弱。
   `result.World` 与 `World.Channel` 在 `SelectCharacterPreparationService` 中恒被构造，
   非空，不引入空引用风险。

- **第4问**：**矛盾处理可达且完整。** 发货档案里没有重复的组名或频道号，所以该路径对发货档案不可达；
   但对运维改过的档案可达，测试用合成文本覆盖。完整性经实测确认为双向：晚出现的坏行会
   作废先读到的好行，先出现的坏行也会挡住随后的好行；重复第三次不会「解封」。

- **第5问**：**`CommandRegistrationTests` 的 `services.AddLogging()` 是忠实修复，不是遮掩。**
   实测删掉这一行后失败信息恰为
   `Unable to resolve service for type 'ILogger<ChannelScriptExperienceSource>'`，
   且生产 `Program.cs` 用 `Host.CreateApplicationBuilder`（自带日志注册）。
   即：缺日志的是测试图而不是生产图，补上之后测试图才与生产一致。
   若不补，反而会出现「测试解析失败、生产解析成功」的假阳性。

- **第6问**：**`ChannelHostStartupTests` 仍然证明它原本要证明的事，并且修得对。**
   它现在断言频道信息帧里的频道条数与**每一条**的端口（记录 48 字节、端口在记录内 +44，
   即 `50 + index*48`，与 `ChannelInfoResponseEncoder` 一致），并断言启动日志里的 `GamePorts`。
   覆盖每一个已配置端口是必要的：默认配置现在有两个游戏端口，若只覆盖第 0 个，
   宿主会去绑发货的 10011——开发机上常有真服务器占着。覆盖后测试只绑自己挑的空闲端口，
   不存在抢占开发者真服务器端口的风险。

- **第7问**：**发货配置与文档不会误导运维。** `experience.json` 保留了一个
   `//ChannelReward` 说明键，明确指出比率在档案里、改这里没用；
   `Docs/HostStartup.md` 的两组副本号与档案逐一核对无误。
   本轮补的那句「绑 `[none]` 的十来行同样是 0（它们连副本组都没有）」经解析结果核对属实：
   确为 12 行（频道 50–54、60–64、68、70），且 `[none]` 在 `[dungeon]` 段中并不存在。
   首轮的文档不准确项已消。

## 剩余非阻断观察

1. **`ChannelHostStartupTests` 的 `count: 2` 与发货 `appsettings.json` 的频道数耦合。**
   将来增删频道会让它失败——失败是响的而不是静默的，可接受，但值得知道。
2. **`Channel:Channels` 的端口与 `Networking:GamePorts` 不再天然同步。**
   以前 `Channels` 缺省由 `GamePorts` 推导；现在两处各写一份，对不上会出现
   「客户端看得见但没人在听」的频道，没有校验也没有启动日志会指出来。发货默认是一致的。
   执行者判为既有隐患、写进后继指引而不在本成果内扩范围——同意。
3. **`ExperienceBonusService` 的类注释已与实现漂移**：仍写着「Each private method reaches one
   domain」，而重构后已无私有方法。
4. **组名匹配用 `OrdinalIgnoreCase`**，而档案两段的组名本来大小写完全一致。宽松匹配本身
   不产生错误答案（真撞上只会判为矛盾、不付），但它是一个未由档案支持的宽容。
5. **流程件**：`sdd-execution.yaml` 声明的 `artifacts.result: ./spec-result.md` 目前不存在。
   属控制器的记账，不影响本次代码判定。
6. **锁的粒度**：`ChannelScriptExperienceSource.Load()` 每次击杀都进一次 `Lock`（命中缓存后即返回）。
   量级可忽略，仅作记录。
7. **交给 verification 的两项**：RQ-007 的「实机一次击杀仍照常发经验」；
   以及 EV-005 要求的实机双频道通告截图。

## 复核方式

- 反射调用 `Parse` 直读发货档案，全量转储频道／组／副本号并与手读比对（临时工程建在
  scratchpad，未进入工作树）。
- 两轮共 10 组变异实验，逐个植入→构建→跑测→按 md5 校验还原；
  本轮重点复跑首轮存活的那一组，并追加「误取监听端口」一组。
- 亲自核验 `junkchan/channel_info.etc` 的内容与 `host.log`，而非采信描述。
- 生产三文件与首轮备份 md5 逐字节相同，确认「生产代码一行未动」属实。
- 还原后复跑 `dotnet build`（0/0）与全量 `dotnet test`（1539 通过 / 2 跳过 / 0 失败）。
- 工作树已完全恢复原状：`git status --porcelain` 与审查开始时逐行相同。
