verdict: passed
kind: rks-review

# 独立评审：spec-002 The character instance

评审对象：工作树 `domain-instances-and-snapshots` 内全部未提交改动（`git diff -M` 的 16 个文件
+ 4 项未跟踪），接受基线 `7478e2e`。评审在全新上下文中进行，不采信执行者的自查结论。

结论：**changes_required**。**没有找到任何行为缺陷**——我能构造的每一条路径上，改后的答案与改前一致，
包括执行者点名担心的冷却键那一处（用与判两侧今天算出的是同一个键，且我用真改真跑证明了这一点）。
一条 A 级发现是**证据**层面的：本成果的头号接缝——「人物由仓库交出」——**全仓没有任何测试碰过它**。
我把 `CharacterDomainService.Get` 改成交出 `characterId + 1` 的人物，全量 1160/2/0 依旧全绿。
契约 SC-B7 要求「对本成果新建立的每一处保证各演示一次故意改坏使其变红」，这一处演示不出来；
执行者为 AC-1 做的那次演示（未发布状态返回伪造角色）打的是 `PlayingCharacter` 的状态检查，
不是「由仓库交出」。补救很小：我已写出那条测试并证明它今天绿、在上述改坏下红。

---

## 一、我实际执行过的独立实验

全部在本工作树内真改真跑。每次改坏前先备份，跑完立刻复原，并与评审开始时记录的 sha256 逐一比对。
所有实验做完后工作树的文件集合与哈希与评审开始时完全一致（见本节末）。
执行者已做过的四处（冷却提到等级前、在场默认翻成地下城、未发布返回伪造角色、判决跳过职业）**一处未重复**。

环境：`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`；测试一律 `-m:1`。

### 基线复核（未改任何东西）

| # | 做了什么 | 结果 |
|---|---|---|
| B1 | `dotnet build Server/86JP.Server.slnx` | **0 警告 0 错误** |
| B2 | `dotnet test Server/86JP.Server.slnx -m:1` | **1160 通过 / 2 跳过 / 0 失败**（Pvf 60 + Application 517 + Host 4 + Infrastructure 100/2skip + Networking 46 + Persistence 230 + Protocol 203）。与执行者声称的数字一致；`Host.Tests` 本次满载下未出现假红 |
| B3 | 三份整记录表征用例 | `git diff --stat -- Server/tests/Dfo.Server.Persistence.Tests/` **输出为空**，三份文件一个字节未动 |
| B4 | 六条既有可用性用例 | `git diff -M` 逐条核对：每一处 `Assert.Equal(ItemUsability.X, …)` 的 `X` 一个未改，只改了构造方式（`ItemUsabilityDomainService` → `PlayingCharacter`）与调用形式（去掉 `Character` 实参） |
| B5 | 新增三个文件的编码 | `ICharacter.cs` / `PlayingCharacter.cs` / `ItemUseJudgementTests.cs` 全部 100% CRLF，首字节非 BOM |

### 改坏实验（真改真跑）

| # | 改了什么（文件:行） | 跑了什么 | 结果 | 说明 |
|---|---|---|---|---|
| E1 | `ItemUseEffectDomainService.cs:40`，把落地一侧的键换成 `judgement.CooldownKeyFor(itemId, rule with { CooltimeGroup = 0 })`——即模拟「判用组键、用起物品键」的分叉 | **全量** `dotnet test Server/86JP.Server.slnx -m:1` | **全绿 1160/2/0** | 两侧冷却键的一致性**无守卫**。今天不会分叉（两侧调的是同一个 DI 单例），但唯一一条端到端冷却用例 `UseStackableHandlerTests.A_second_use_inside_the_cooldown_is_refused` 用的是 `CooltimeGroup: 0` 的药水，两种推导恰好重合 |
| E2a | `PlayingCharacter.cs:126/131`，把职业检查提到等级之前 | Application.Tests | **全绿 517** | 等级 vs 职业的先后**无守卫** |
| E2b | 同上，把地点检查提到职业之前 | Application.Tests | **红 1**：`ItemUseJudgementTests.A_job_mismatch_wins_over_a_place_that_is_also_wrong` | 职业 vs 地点的先后**有守卫**（本轮新增的那条） |
| E2c | 同上，把冷却检查提到地点之前（冷却不再最后问） | Application.Tests | **全绿 517** | 「冷却最后问」只被钉在等级那一对上，对地点不成立 |
| E3 | `PlayingCharacter.cs:70-73`，把「会话目录取不到」那一支从 `return null` 换成抛异常 | **全量** | **全绿 1160/2/0** | 身份解析第一步（会话目录）失效的路径**全仓无人构造**；被守住的只有第二步（运行时目录未发布） |
| E4 | 临时探针用例（跑完已删）：`FindByCharacter` 恒返回 `null` 的会话目录 | 探针 2 条 | **全绿** | 行为本身**正确**：第一步失效答 `CharacterStateUnavailable`；且未知物品仍先于它答 `NotAUsableItem` |
| E5 | `PlayingCharacter.cs:41/48`，把「状态取不到」的判断提到「档案读不出」之前 | Application.Tests | **全绿 517** | `NotAUsableItem` 先于 `CharacterStateUnavailable` 这条先后**无守卫**（行为未变，由 E4 探针证实） |
| E6 | `PlayingCharacter.cs:57`，删掉 `rule.CooldownMilliseconds > 0 &&` 这个闸门 | Application.Tests | **全绿 517** | 这条从旧文件搬过来、落在**实例**一侧的规则**无守卫** |
| E6b | 临时探针（跑完已删）：物品 A 组 70 / 3 秒、物品 B 组 70 / 自身不声明冷却，先起 A 的组冷却 | 探针 2 条 | **全绿** | 闸门是**吃劲的且可构造**：人物答 B `Allowed`；而 `Judge` 拿同一条 rule + `IsCoolingDown: true` 直接问，答 `OnCooldown`——两半对同一个问题给出不同答案 |
| E7 | `CharacterDomainService.cs:28`，`Get` 改为交出 `new CharacterId(characterId.Value + 1)` 的人物 | **全量** | **全绿 1160/2/0** | **A-1**：仓库交出人物这条接缝全仓无测试。仓库交错人、`ApplicationModule` 的工厂接错 id，全量都不会红 |
| E8 | 临时探针（跑完已删）：用真 `ApplicationModule` 建容器 → `provider.GetRequiredService<ICharacterDomainService>()` → `Get(60)` → `CanUseAsync` | 探针 1 条 | **绿**（未改动的代码上） | 顺带证实生产装配是通的：工厂 lambda 六个依赖全部可解析，端到端答 `Allowed` |
| E8b | 保留 E8 的探针，重新施加 E7 的改坏 | 探针 1 条 | **红**：`Assert.Equal() Failure: Values differ` | 证明 A-1 的补救是一条约 40 行的测试即可，且它确实会红 |

### 复原核对

- `sha256sum -c` 对 6 个被动过或相邻的文件全部 `OK`（`PlayingCharacter.cs`、`ItemUseEffectDomainService.cs`、
  `ICharacter.cs`、`CharacterDomainService.cs`、`UseStackableSlice.cs`、`ItemUseFixtures.cs`）。
- 三个临时探针文件（`ReviewProbeTests.cs`、`ReviewProbe2Tests.cs`、`ReviewProbe3Tests.cs`）已删除，
  `git status --porcelain` 与评审开始时逐行一致：16 项改动 + 4 项未跟踪，无残留。
- 复原后重跑：`dotnet build` **0 警告 0 错误**；`dotnet test … -m:1` **1160 / 2 / 0**。
- 未提交、未 merge、未清理工作树；未触碰 `Tool/` 与 `.worktrees/legacy-reference/`。

---

## 二、发现

### A 级（阻塞）

#### A-1 「人物由仓库交出」这条接缝没有任何执行证据，且按 SC-B7 演示不出来

- **文件与行**：`Server/src/Dfo.Server.Application/Characters/CharacterDomainService.cs:28`
  （`public ICharacter Get(CharacterId characterId) => playingCharacter(characterId);`）；
  `Server/src/Dfo.Server.Application/ApplicationModule.cs:88-96`（`characterId => new PlayingCharacter(characterId, …)`）；
  `Server/src/Dfo.Server.Abstractions/Characters/ICharacterServices.cs:71`。
- **可复现构造**：把 `CharacterDomainService.cs:28` 改成
  `=> playingCharacter(new CharacterId(characterId.Value + 1));`，
  跑 `dotnet test Server/86JP.Server.slnx -m:1` → **1160 通过 / 2 跳过 / 0 失败**（实验 E7）。
  仓库把「谁」这件事整个搞错，全量不动声色。
- **为什么没被挡住**：`UseStackableCommandHandler` 的用例走的是 `ItemUseFixtures.cs:260` 的
  `OneCharacter` 假仓库（它忽略入参、恒返回同一个人物）；`CharacterItemUseTests` 直接 `new PlayingCharacter(...)`；
  `CharacterLifecycleServiceTests` 虽然经 DI 拿到 `ICharacterDomainService`，但从不调用 `Get`。
  于是**真 `CharacterDomainService.Get` 与 `ApplicationModule` 的工厂 lambda 在整套测试里从未被执行过一次**。
- **分级理由**：这不是偏好问题，是契约条款没满足。
  - SC-B1 的第一句就是「存在人物实例，**由仓库交出**」；SC-B7 要求「对本成果**新建立的每一处保证**各演示一次
    故意改坏使其变红」。这条接缝是本成果新建立的头号保证，而我用实验证明了它**改坏也不会红**。
  - 执行者列出的四次演示中，为 AC-1 做的那次是「让未发布状态也返回一个伪造角色」——它打的是
    `PlayingCharacter.cs:48` 的状态检查，与「由仓库交出」无关。因此 SC-B7 / AC-8 对 AC-1 的覆盖只到一半。
  - 后果不是理论的：这条接缝一旦接错（工厂传错 id、`Get` 被改写、日后加装饰器忘了转发），
    表现是**全服物品使用静默地问错人**，而全量测试全绿。
- **必须修什么**：加一条经由仓库取人物的用例，并演示它会红。我已验证这条用例存在且可行——
  实验 E8 的探针用真 `ApplicationModule` 建容器、`GetRequiredService<ICharacterDomainService>()`、
  `Get(new CharacterId(60))`，断言 `asked.Id`、断言它答 `Allowed`、再断言 `Get(new CharacterId(61))`
  答 `CharacterStateUnavailable`；它在未改动的代码上绿（E8），在上面那处改坏下红（E8b）。
  该探针已删除，不留在工作树里——它是发现的证据，不是我替执行者写的成果。

### B 级（不阻塞，但建议在本轮或明确记档后处理）

#### B-1 用与判两侧的冷却键一致性，从「结构上不可能分叉」降级为「靠装配保证」，且无守卫

- **文件与行**：`Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs:57`（`CooldownKeyFor` 成为接口方法）；
  `Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs:19,40`；
  `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:58`；
  `Server/src/Dfo.Server.Application/ApplicationModule.cs:122`。
- **结论先说**：**今天不会分叉**。`IItemUseJudgementService` 只有一处 `TryAddSingleton` 注册，
  判与用调的是同一个实例的同一个方法；`UseStackableHandlerTests.cs:272,283` 的夹具也把同一个 `judgement`
  同时交给两侧。执行者点名担心的「一次使用启动一个与判定所查不同的键」在改后的代码里**不成立**。
- **但**：改前 `CooldownKeyFor` 是 `static`，两侧分叉在结构上不可能；改后它是可注入接口上的实例方法，
  分叉变成一件靠 DI 注册与夹具装配来保证的事，而**没有任何测试盯着这件事**。
  实验 E1：只让落地一侧算出不同的键（`rule with { CooltimeGroup = 0 }`），全量 1160/2/0 全绿。
  唯一一条端到端冷却用例用的是不带组的药水，两种推导恰好重合，所以照不到这一格。
- **分级理由**：无行为缺陷、无回归，仅是防护降级，故为 B。若要修，两条路任选：
  给 `UseStackableHandlerTests` 补一条「带 `CooltimeGroup` 的物品，用过之后第二次被拒」的端到端用例；
  或把键推导从判决接口挪回一个纯函数（例如 `ItemCooldownKey.For(itemId, rule)`），
  顺带解掉「落地服务为了取一个键而依赖一个判决服务」这层多余耦合。

#### B-2 判决那一半不是规则的全集：`Judge` 与人物对同一条 rule 会给出不同答案

- **文件与行**：`Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:57-58`
  （`rule.CooldownMilliseconds > 0 &&` 这个闸门留在实例一侧）；对照
  `PlayingCharacter.cs:141`（`Judge` 只看 `candidate.IsCoolingDown`，不再回看 `rule.CooldownMilliseconds`）。
- **可复现构造**（实验 E6b，探针已删）：物品 A `CooltimeGroup = 70, CooldownMilliseconds = 3000`；
  物品 B `CooltimeGroup = 70, CooldownMilliseconds = 0`。起 A 的组冷却后：
  经人物问 B → `Allowed`（与改前一致，正确）；把同一条 B 的 rule 与 `IsCoolingDown: true` 直接交给
  `Judge` → `OnCooldown`。两半对同一个问题不同答。
- **且这条搬过来的闸门本身无守卫**：实验 E6 删掉 `rule.CooldownMilliseconds > 0 &&`，Application.Tests 全绿 517。
- **分级理由**：**行为未变**（今天 `Judge` 只有 `PlayingCharacter` 一个调用方，闸门在它上游生效），
  C-1 与 SC-B4 的六种判定逐条不变，故不阻塞。但它使
  Spec RQ-3「判决那半只吃它要比较的东西……吐一个判决」与设计文档新增的
  「item usability is now two halves, an instance that supplies identity and a judgement」
  两句话在字面上不准确：实例不止供身份，它还做掉了 `NotAUsableItem`，并替判决把冷却声明的闸门关了。
  修法最省：把闸门移进 `Judge`（`rule.CooldownMilliseconds > 0 && candidate.IsCoolingDown`），
  再补一条 `Judge` 层的用例钉住它。
- **关于「`rule is null || !rule.IsReadable` 留在实例里是否违反 R-6」**（评审要求点名的问题）：
  **不违反**。R-6 约束的是规则服务（不解析身份、不读库），`ItemUseJudgementService`
  逐字满足——它拿不到任何能去找人的东西。R-6 没有说实例不许做判断。
  而且这条留在实例里是**有理由的**：`NotAUsableItem` 必须先于 `CharacterStateUnavailable` 报出，
  而 `Judge(rule, candidate)` 的签名要求 rule 非空、candidate 已存在，
  把它搬进去就得让 `Judge` 接受可空 rule 和不存在的候选人。这是可辩护的取舍。
  真正没有这层辩护的是上面那个冷却闸门——它比较的两样东西 `Judge` 手里都已经有了。

#### B-3 判定次序只有两对被守住

- **文件与行**：`Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:126,131,136,141`。
- **实测**：四条比较共六对先后。有守卫的两对——等级 > 冷却（既有
  `CharacterItemUseTests.A_permanent_reason_is_reported_ahead_of_one_that_will_expire`，执行者已演示会红）、
  职业 > 地点（本轮新增 `ItemUseJudgementTests.A_job_mismatch_wins_over_a_place_that_is_also_wrong`，
  我实测 E2b 会红）。无守卫的：等级 > 职业（E2a 全绿）、冷却排在地点之前（E2c 全绿）。
- **分级理由**：契约 SC-B4 只要求「另有一条证明判定次序未变（等级不足与冷却中同时成立时仍报等级不足）」，
  这条在；交付的代码次序也一字未改。故为 B。但 C-3 写的是「永久性的原因先问、**冷却最后问**」，
  而「冷却最后问」目前只对等级成立——把冷却提到地点之前，全量不会红。
  新增的 `ItemUseCandidate` 让补齐这些用例变得很便宜（都是值，不用构造角色），值得顺手补两条。

#### B-4 身份解析第一步（会话目录）失效的路径全仓无人构造

- **文件与行**：`Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:69-77`。
- **实测**：E3 把 `if (session is null) return null;` 换成抛异常，全量 1160/2/0 全绿——
  说明该分支从未被执行。E4 探针证实**行为是对的**：会话目录取不到时答 `CharacterStateUnavailable`，
  与第二步（运行时目录未发布）同一个答案，与改前一致。
- **分级理由**：这是从旧 `ItemUsabilityDomainService.FindCharacter` 原样继承下来的缺口，
  不是本轮引入的回归；契约也没有要求两步各有用例。故 B。

#### B-5 `NotAUsableItem` 先于 `CharacterStateUnavailable` 这条先后无守卫

- **文件与行**：`Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:41,48`。
- **实测**：E5 把两个判断对调，Application.Tests 全绿 517。行为未变（E4 探针的第二条断言证实
  「未知物品 + 角色未选中」今天答 `NotAUsableItem`）。
- **分级理由**：同 B-4，继承自旧实现的缺口，非本轮回归。但它恰恰是
  `ItemUsability.CharacterStateUnavailable` 的注释花了一整段解释「不能折进档案自己的拒绝里」的那件事
  （`Server/src/Dfo.Server.Abstractions/Inventory/IItemUsabilityDomainService.cs:26-34`），一条注释级理由而无断言。

#### B-6 契约 revision 2 之后，三份工件仍在陈述被推翻的形状

- `attempts/spec-002-the-character-instance/spec.md`：frontmatter `contract_revision: 1`；
  且 Outcome 段仍写「它在选人时诞生、退出时消失，与背包同处同时」——
  **与同一份文件里的 RQ-1**（「它不需要一套自己的生命周期」）**直接矛盾**。
- `attempts/spec-002-the-character-instance/sdd-execution.yaml`：`contract.revision: 1`。
- `.rks-agent/tasks/domain-instances-and-snapshots/multi-spec-map.md`，Spec Attempts 第 2 行：
  成果描述仍是「由人物仓库交出，**选人时造、退出时清**」。
- **分级理由**：不影响代码，故 B。但本轮评审的核心议题之一就是 R-4 与契约 rev-2 的改写，
  而三份工件仍在断言被丢掉的那个形状；根契约 SC-10 要的正是「被推翻的条目已在文档中修订而非留成矛盾」。
  设计文档（权威）已经改对了，落下的是这三处。

#### B-7 设计文档 R-6 新增的「nuance」段少说了两个参数

- `Docs/Refactor/DomainInstanceDesign.md:202-205` 只提 `IDungeonEffectRuleService.CanCarryOut` 的
  `int recordedAccountId`；该签名同时还收 `int accountId` 与 `int characterId`
  （`Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryRules.cs:94-101`）。
  论点没错（弱类型的数字是被比较的值，不是「去找」的指令），但举证举窄了——
  漏掉的那两个恰恰是名字与强类型最像的两个，是这段话最需要覆盖的反例。

#### B-8 新类型的落位与文件名残留

- `Server/src/Dfo.Server.Abstractions/Inventory/IItemUsabilityDomainService.cs` 里已经没有
  `IItemUsabilityDomainService` 了，只剩 `ItemUsability` 枚举，文件名指向一个不存在的类型。
- `IItemUseJudgementService` 与 `ItemUseCandidate` 落在
  `Abstractions/Characters/ICharacter.cs:45,54`（命名空间 `Dfo.Server.Abstractions.Characters`），
  而它们返回的 `ItemUsability` 在 `Dfo.Server.Abstractions.Inventory`；
  实现 `ItemUseJudgementService` 则落在 `Application/Characters/PlayingCharacter.cs:107`。
  都不违反任何约束，但「按名字找判决服务」会连着找三个地方。

#### B-9 `EquipmentEligibilityCharacterFactsTests` 用 `null!` 填工厂

- `Server/tests/Dfo.Server.Application.Tests/Characters/EquipmentEligibilityCharacterFactsTests.cs:32`。
  今天这条路不会造人物，所以是安全的；但一个「会抛且带一句话」的工厂能把这件事说出来，
  与同一轮里其他三处 stub 的写法
  （`throw new NotSupportedException("This stand-in answers no questions about a character.")`）也一致。

---

## 三、评审要求点名的四项判断

### 1. 冷却键：用与判两侧仍然一致吗？——**一致**

改后 `PlayingCharacter.cs:58` 与 `ItemUseEffectDomainService.cs:40` 调的是**同一个方法**
（`IItemUseJudgementService.CooldownKeyFor`），DI 里只有一处注册（`ApplicationModule.cs:122`，单例），
夹具（`UseStackableHandlerTests.cs:272,283`）也把同一个实例交给两侧。
键推导逻辑与被删的 static 逐字相同（组 > 0 用组键，否则用物品键）。
执行者担心的「无限使用」在改后的代码里构造不出来。
唯一的问题是这条一致性没有守卫（B-1），我用 E1 把它证到了。

### 2. R-4 的改写站得住吗？给出的理由属实吗？——**站得住，理由属实**

我去代码里核实了改写所依据的事实，它成立：

- `ICharacterContainerDomainService.GetContainers(characterId, type)` 的契约就是
  「Every container the character holds of that type, in registration order」
  （`Server/src/Dfo.Server.Abstractions/Inventory/ICharacterContainerDomainService.cs:43-44`），
  `UseContainer(characterId, containerId)` 是
  「Makes an already-registered container the current one for its type」（`:53-55`）。
  实现 `CharacterContainerDomainService.CharacterContainers` 确实同时维护 `registered` 列表与
  `current` 绑定，并在 `Remove` 时回落到同类型第一个不可移除的容器
  （`Server/src/Dfo.Server.Application/Inventory/CharacterContainerDomainService.cs:57-113`）。
  所以「一个角色可持有同类型的多个背包并在其间切换，注册表承载的是当前用哪一个」**不是事后合理化**，
  是接口自己写下的语义。
- 「人物没有这回事」也成立：`ICharacter` 上没有任何可选项，`Get(characterId)` 一人一实例。
- 旧 R-4 的理由（分配开销）确实与同一文档顶部的规模前提冲突，根契约 Out 一节也写明「性能不是本任务的判据」。
  用一条可在代码里核实的判据（有没有一个跨调用存活的事实要记）换掉一条文档自己否认的判据，**是加强而非退让**。
- 自洽性：改写后的 R-4 与 R-1（实例不持状态）、R-3（实例是入口）、R-5（仓库两种产物）都不冲突。
  「answers only while the character is selected — not because a lifecycle says so, but because what
  it reads exists only then」这句我也核实了：退出路径
  `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:124-126` 依次
  `runtimes.RemoveExact` → `activeCharacters.ClearExact` → `sessions.TryRemoveExact`，两级目录都清掉，
  所以跨过退出还攥着的实例确实只会答 `CharacterStateUnavailable`。
- 一处措辞挑剔（不构成发现）：规则标题以「有没有注册」作判据，而注册本身是结果不是原因；
  正文说清了真正的判据是「有没有一个跨调用存活的事实」。标题不如正文准。

### 3. 契约 revision 2 是不是把判据往已做到的地方挪？——**不是，但证据门槛确实降了，而 A-1 正落在降的那一格**

- **不是搬门柱**的理由：改写依据的是一条**独立于实现结果**、且我能在代码里核实的性质
  （背包需要注册表因为要回答「当前用哪一个」；人物没有这个问题），不是「我做成了什么就把标准改成什么」。
  改写还同步写回了设计权威 R-4，而不是只改契约绕过文档——
  这正是父契约 Assumptions 要求的做法（「应修订该文档并记录理由，而不是绕过它执行」）。
- **可证伪性没有变弱**的一半：rev-1 的「退出时清」若照搬背包，会验成「生命周期服务调了某个 Clear」；
  rev-2 的「只在角色被选中时能回答问题」验的是**答案**（`CharacterStateUnavailable`），更难糊弄，
  而且这条今天有用例
  （`CharacterItemUseTests.A_character_whose_state_is_not_published_yet_is_not_the_players_fault`）。
- **但门槛确实降了的一半**：rev-1 的「选人时造」必然要求有人在选人路径上调用构造，
  从而必然被那条路径的用例覆盖；rev-2 把它换成「由仓库交出」，而 Required evidence 只写了
  「**能指出**该类型与交出它的方法」——一条「指出即可」的判据。结果就是 A-1：
  这条接缝最后既没有用例、也没有可证伪演示。
  所以我的判断是：**改写的推理是好的，证据条款写松了**，而本轮的结果恰好证明了松在哪里。
  补上 A-1 那条用例之后，rev-2 就既有独立理由也有独立证据了。

### 4. 每次 `Get` 新造一个 `PlayingCharacter` 会不会带来正确性问题？——**不会**

（按要求不评性能。）

- 实例只持 `characterId` 与六个依赖引用，全部是 DI 单例（`ApplicationModule.cs:121,152-154`
  与 `InfrastructureModule.cs:62`），自身不缓存任何状态（`PlayingCharacter.cs:21-33`），
  每次提问都重读目录。符合 R-1 与契约 C-5。
- 全仓只有一个生产调用方 `UseStackableSlice.cs:252`，它 `Get(...)` 之后立刻提问，
  不保存、不跨事务复用、不做引用比较。全仓搜不到任何依赖「同一个角色拿到同一个实例」的地方。
- 跨会话也安全：退出清掉两级目录（见上），旧实例只会答 `CharacterStateUnavailable`。
- `CharacterDomainService` 的工厂 lambda 捕获的是根 provider，被捕获的六个依赖都是单例，
  不存在 captive dependency 问题（全仓只有 `CommandDispatchingExtensions.cs:59-60` 用 `AddScoped`，
  与此无关）。

---

## 四、被删文件的规则逐条对照（要求 3）

`git diff` 中 `Server/src/Dfo.Server.Application/Inventory/ItemUsabilityDomainService.cs` 整文件删除（97 行）。
它承载的每一条判断都找到了对应物，**没有丢**：

| 旧位置 | 承载的判断 | 新位置 | 是否等价 |
|---|---|---|---|
| `:36-37` | `rule is null \|\| !rule.IsReadable` → `NotAUsableItem`（含那段「不认得的限制不等于没有限制」的理由） | `PlayingCharacter.cs:36-44`，注释逐字保留 | ✓ |
| `:39-41` | `FindCharacter` 为 null → `CharacterStateUnavailable`，且**排在**档案判断**之后** | `PlayingCharacter.cs:46-51` | ✓（次序保留；E5 显示这条次序无守卫） |
| `:46-47` | `Level < MinimumLevel` → `LevelTooLow` | `ItemUseJudgementService.Judge`，`PlayingCharacter.cs:126` | ✓ |
| `:49-50` | `!Jobs.Allows(CharacterJobFamilies.Resolve(job))` → `JobMismatch` | 解析 job 家族在 `PlayingCharacter.cs:64`，比较在 `:131` | ✓ |
| `:52-53` | `!Places.Allows(PlaceOf(characterId))` → `PlaceNotAllowed` | 取地点在 `PlayingCharacter.cs:65`（调 `:93` 的 `Place()`），比较在 `:136` | ✓ |
| `:55-58` | `CooldownMilliseconds > 0 && IsCoolingDown(...)` → `OnCooldown`，否则 `Allowed` | **拆成两处**：闸门 `PlayingCharacter.cs:57`，判决 `:141` | 行为等价，但判决半单独调用会不等价（**B-2**） |
| `:44-45` | 「永久性的原因先问、冷却最后问」的注释与理由 | `ICharacter.cs:60-68` 与 `PlayingCharacter.cs:120-123`，理由随比较一起搬了 | ✓（计划 CK-4 要求的正是这个） |
| `:61-65` | `static CooldownKeyFor` | `ItemUseJudgementService.CooldownKeyFor`（`PlayingCharacter.cs:109-114`），逻辑逐字相同，多了一个 `ThrowIfNull` | ✓（另见 B-1） |
| `:67-74` | `FindCharacter`：会话目录 → 运行时目录两步 | `PlayingCharacter.cs:69-77`，逐字相同 | ✓（第一步无守卫，**B-4**） |
| `:76-93` | `PlaceOf` 与「在场记录缺失按村庄算」的整段理由 | `PlayingCharacter.cs:79-97`，注释逐字保留 | ✓ |
| `:14-22` | 类注释里「这里不写任何东西，起冷却属于效果」 | `ICharacter.CanUseAsync` 的注释 `ICharacter.cs:28-33` | ✓ |

---

## 五、超范围检查（要求 5）

契约 Out 五项逐条核对：

| Out 项 | 是否被碰 | 判断 |
|---|---|---|
| 快照重塑（`ActiveCharacterState` 改领域记录） | 未碰 | ✓ |
| 人物的写入动词 | 未碰（`ICharacter` 上只有 `Id` 与 `CanUseAsync`） | ✓ |
| `ItemUseEffectDomainService.ApplyAsync` 的归属 | `ApplyAsync` 本体一字未动；改的是**构造函数**（`:19` 注入判决服务）与 `PlanAsync` 里那一行取键（`:40`） | **不算越界** |
| 跨实例操作 | 未碰 | ✓ |
| 客户端错误码及其翻译 | `StatusFor` 与 `ItemUseStatus` 一字未改（`UseStackableSlice.cs:274-286`） | ✓ |

关于第三项的理由：Out 写的是「**归属**」，即 `ApplyAsync` 该不该搬到背包或人物身上——
那是设计文档三个未决项之三，本轮明确不裁定，文档里也照旧留着
（`DomainInstanceDesign.md` 末尾「Two things this document deliberately does not decide」仍列着它）。
构造函数那一改是**删掉旧类之后取键的唯一去处**，属于随改动而来的机械后果，不是归属的搬动。
故不判为越界。但它确实新造了一层「落地服务依赖判决服务」的耦合，而落地并不判决——
这一点已并入 B-1 记录，那里也给了不需要这层耦合的替代做法。

---

## 六、SC 与 AC 逐条核对

### 契约 Success Conditions

| ID | 判定 | 依据 |
|---|---|---|
| SC-B1 | **部分满足** | 类型 `ICharacter`（`ICharacter.cs:23`）/ `PlayingCharacter`（`PlayingCharacter.cs:21`）与交出它的方法 `ICharacterServices.cs:71` / `CharacterDomainService.cs:28` 都能指出；「未被选中时答 `CharacterStateUnavailable`」有用例。**但「由仓库交出」这一半没有任何用例经过它**（E7），见 A-1 |
| SC-B2 | 满足 | `UseStackableCommandHandler` 的构造参数由 `IItemUsabilityDomainService` 换成 `ICharacterDomainService`（`UseStackableSlice.cs:224`），提问改为 `characters.Get(command.CharacterId).CanUseAsync(...)`（`:252-253`） |
| SC-B3 | 满足 | 逐个清点全仓 10 个规则服务，方法签名中**无一**出现 `CharacterId` 或 `AccountId`：`IGoldLimitRuleService.UpgradeLevel(int,int)`；`IExpertSkillContractRuleService.HasRunOut(...)` / `.CapsFor(...)`；`IDungeonEffectRuleService.CanCarryOut(...)`（全 `int`/`long`/`string`）/ `.GrantLuckyStar` / `.SplitExperience` / `.AccumulateHonour`；`IItemArrivalRuleService.ForDailyRefill` / `.ForGrant`；`IDailyRefillRuleService.PeriodOf` / `.IsDue` / `.GrantFor`；`IItemSlotRuleService.CanOccupyMainSlot`；`IDungeonLayoutRuleService.ResolveAsync(int, DungeonEntryFacts, int, ct)`；`IEntryPlacementRuleService.ResolveAsync` / `.CanEnterTownAsync` / `.DeclaresAreaAsync`（`ChannelEntryIdentity` 是频道身份，非人物/账号）；`IWorldContentEntryRuleService.LoadAsync(WorldContentSubject, ct)`（`WorldContentSubject` 只有等级、职业、进度，无身份）；`IItemUseJudgementService.CooldownKeyFor` / `.Judge`。仓库类与注册表带身份，按契约不在此列 |
| SC-B4 | 满足 | 六种判定 + 允许 = 七条用例俱在（`CharacterItemUseTests`），`git diff -M` 证明期望值一字未改；次序用例 `A_permanent_reason_is_reported_ahead_of_one_that_will_expire` 在，且执行者已演示会红。附注 B-3：只有两对次序有守卫 |
| SC-B5 | 满足 | 我独立复核：`dotnet build` 0 警告 0 错误；`dotnet test Server/86JP.Server.slnx -m:1` = 1160 / 2 / 0。相对 spec-001 接受时 1151 净增 9，与 `ItemUseJudgementTests` 的 9 条一一对上 |
| SC-B6 | 满足 | `DomainInstanceDesign.md` 中「Whether the character instance is also the runtime session state」这条 bullet 已删除；新增 `## Settled by the character-side implementation`（`:318`），写明结论（实例不是运行时会话态，它读它；目录仍是权威）、理由（合并会挪动权威，而权威变了答案就变）、以及所依赖的前提与何时重开（`:330-335`）。不留待办 |
| SC-B7 | **不满足** | 执行者的四次演示我逐条查过归属，覆盖的是判定次序、地点默认、状态检查、职业比较。本成果**新建立**的头号保证「人物由仓库交出」演示不出来——E7 证明改坏它全量仍绿。见 A-1 |

### Spec Acceptance

| ID | 判定 | 依据 |
|---|---|---|
| AC-1 | **部分满足** | 同 SC-B1。「能指出类型与交出它的方法」✓；「一条用例证明角色未被选中时的答案与今天一致」✓，但那条用例直接 `new PlayingCharacter`，不经仓库 |
| AC-2 | 满足 | 处理器不再持有可用性服务；六条既有用例期望值一字不改而全绿（我独立跑过，且逐条核对了 `git diff -M`） |
| AC-3 | 满足 | 清点见 SC-B3；`ItemUseJudgementTests` 全部 9 条不构造任何角色、不建会话、不碰目录与库，判决半确实可单独调用。附注 B-2：判决半不是规则全集 |
| AC-4 | 满足 | 七种结果逐条可构造；次序守卫在且执行者实测会红（我另测出两对无守卫，记为 B-3，不影响 AC-4 的字面要求） |
| AC-5 | 满足 | `PlayingCharacter.Place()`（`:93-97`）与旧 `PlaceOf` 逐字相同，默认仍是村庄；守卫用例在；执行者已演示会红（把默认翻成地下城红两条） |
| AC-6 | 满足 | 同 SC-B6 |
| AC-7 | 满足 | 同 SC-B5 |
| AC-8 | **不满足** | AC-8 要求对 AC-1、AC-3、AC-4、AC-5 各演示一次。AC-4、AC-5 的演示成立；AC-3 的演示（判决跳过职业检查）成立；**AC-1 的演示只覆盖了「未被选中时的答案」，未覆盖「由仓库交出」**，而后者正是 AC-1 的第一句。见 A-1 |

### 约束核对（评审要求的「违反即为发现」清单）

| 约束 | 判定 | 依据 |
|---|---|---|
| 三份整记录表征用例期望值不得改 | 未违反 | `git diff --stat -- Server/tests/Dfo.Server.Persistence.Tests/` 输出为空 |
| 六条原有可用性用例期望值不得改 | 未违反 | `git diff -M` 逐条核对，只改构造与调用形式 |
| 判定先后次序不得改变 | 未违反 | 四条比较的相对次序与被删文件逐条一致 |
| 不得改判定所读的权威来源 | 未违反 | 仍是 `ISelectedSessionDirectory` → `ISelectedSessionRuntimeDirectory`，`PlayingCharacter.cs:69-77` 与旧 `FindCharacter` 逐字相同 |
| C# 文件 CRLF、无 BOM UTF-8 | 未违反 | 新增三文件 100% CRLF、无 BOM |
| net10.0、`TreatWarningsAsErrors` | 未违反 | 构建 0 警告 0 错误 |
| 不得改动 `Tool/`、`.worktrees/legacy-reference/` | 未违反 | `git status` 无相关条目 |
| 不提交、不 merge、不清理工作树 | 未违反（评审侧） | 我的全部改动已逐字节复原，sha256 全 `OK`，`git status` 与评审开始一致 |

---

## Blocking Findings

**A-1 — 「人物由仓库交出」这条接缝没有执行证据，SC-B7 / AC-8 对 AC-1 的演示只做了一半。**

必须修：

1. 加一条**经由仓库**取人物的用例。它必须真正走过 `CharacterDomainService.Get`
   （最好连 `ApplicationModule` 的工厂 lambda 一起走过），而不是直接 `new PlayingCharacter`，
   也不是走 `ItemUseFixtures.OneCharacter` 那个忽略入参的假仓库。
   最小可行形状（我已实测其今天绿、改坏后红）：用真 `ApplicationModule` 建 `ServiceCollection`
   （补三个 stub：`ICharacterStatDefinitionSource`、`IInitialSkillDefinitionSource`、
   `IItemUseRuleDefinitionSource`）→ `GetRequiredService<ICharacterDomainService>()` →
   注册会话并 `Publish` 一份运行时状态 → 断言 `Get(id).Id == id`、断言它答 `Allowed`、
   再断言 `Get(另一个 id)` 答 `CharacterStateUnavailable`。
2. 对这条新保证补一次可证伪演示并记录：把 `CharacterDomainService.cs:28` 改成
   `playingCharacter(new CharacterId(characterId.Value + 1))`，证明该用例变红，复原后变绿。
3. 相应更新自查证据，把「四次演示」的覆盖面说清楚——特别是不要把「未发布状态返回伪造角色」
   记成 AC-1 的完整演示。

B 级各项不阻塞本轮通过；若不在本轮处理，建议至少把 B-6（三份工件仍陈述被推翻的形状）
与 B-2（`Judge` 与人物对同一条 rule 不同答）明确记档，前者关系到根契约 SC-10，
后者关系到设计文档新增那一节里「两半」这个说法的准确性。

---

## 第二轮（整改复评）

**verdict: passed**

本文件 frontmatter 的 `verdict` 已由 `changes_required` 改为 `passed`。上方「## Blocking Findings」
一节是**第一轮的记录，原样保留**；其 A-1 已在本轮闭合，证据见下。

评审对象：同一工作树 `domain-instances-and-snapshots` 内的全部未提交改动（16 项已跟踪 + 5 项未跟踪），
接受基线 `7478e2e`。在全新上下文中进行，不采信执行者的自查结论；执行者做过的三处演示**一处未重复**，
下表全部是我自己另写另跑的。
环境：`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`；测试一律 `-m:1`。

**结论**：第一轮 A-1 **已闭合**——新用例打的是真接缝（它传的是工厂而不是现成实例），我独立改坏
`CharacterDomainService.Get` 后它确实变红，而第一轮同一改坏是全量全绿。冷却闸门搬进判决**行为逐条不变**，
第一轮实证的那处「两半对同一条 rule 不同答」确实消失，且**不构成越界**。
六行 `[Theory]` 覆盖的是**五对而不是六对**：缺的那一对（等级 vs 冷却）在 `ItemUseJudgementTests` 里原有的
守卫恰好被本轮同一处改动打成了空转——这是本轮引入的新问题，但那一对在人物层仍有守卫且我实测会红，
故记为 B 而非 A。除此之外，整改自身未引入任何行为缺陷。

---

### 一、我实际执行过的独立实验

全部在本工作树内真改真跑。评审开始时对 `Server/`、`Docs/`、`.rks-agent/tasks/domain-instances-and-snapshots/`
下 **572 个源文件**取了 sha256；所有实验做完后 `sha256sum -c` **572 个全部 `OK`**，
`git status --porcelain` 与开始时逐行一致，两个临时探针文件已删除。

#### 基线复核（未改任何东西）

| # | 做了什么 | 结果 |
|---|---|---|
| R2-B1 | `dotnet build Server/86JP.Server.slnx` | **0 警告 0 错误** |
| R2-B2 | `dotnet test Server/86JP.Server.slnx -m:1` | **1171 通过 / 2 跳过 / 0 失败**（Pvf 60 + Application 528 + Host 4 + Infrastructure 100/2skip + Networking 46 + Persistence 230 + Protocol 203）。与执行者声称的数字一致；`Host.Tests` 满载下未出现假红 |
| R2-B3 | 三份整记录表征用例 | `git diff --stat -- Server/tests/Dfo.Server.Persistence.Tests/` **输出为空** |
| R2-B4 | 六条既有可用性用例 | `git diff -M` 逐条核对：每一处 `Assert.Equal(ItemUsability.X, …)` 的 `X` 一个未改，只改了构造方式与调用形式 |
| R2-B5 | 新增/改动的 7 个 C# 文件编码 | `ICharacter.cs` / `PlayingCharacter.cs` / `ItemUseJudgementTests.cs` / `CharacterRepositoryProductsTests.cs` / `UseStackableHandlerTests.cs` / `ItemUseFixtures.cs` / `CharacterItemUseTests.cs` 全部 **100% CRLF、首字节非 BOM** |

#### 改坏实验与探针

除 R2-2 外均只跑 `Dfo.Server.Application.Tests`（528 条）。

| # | 改了什么（文件:行） | 结果 | 说明 |
|---|---|---|---|
| R2-1 | `CharacterDomainService.cs:28` → `playingCharacter(new CharacterId(characterId.Value + 1))` | **红 2**：`CharacterRepositoryProductsTests.The_repository_hands_back_the_character_it_was_asked_for`、`.The_character_it_hands_back_says_which_character_it_is` | **第一轮 A-1 闭合**。第一轮同一改坏是全量 1160/2/0 全绿 |
| R2-2 | `ApplicationModule.cs:88-96` 的工厂 lambda 整个换成 `characterId => throw new NotSupportedException(...)`。跑**全量** | **全绿 1171/2/0** | 组合根那一段仍无任何测试执行过。见 B2-2 |
| R2-3 | `Judge` 次序：冷却提到**最前** | **红 3**：Theory 第 3、4 行 + `CharacterItemUseTests.A_permanent_reason_is_reported_ahead_of_one_that_will_expire`。而 `ItemUseJudgementTests.A_permanent_reason_wins_over_one_that_will_expire` **仍绿** | 等级 > 冷却在**人物层**有守卫；Judge 层那条同名用例已空转。见 B2-1 |
| R2-4 | 次序：等级、**地点、职业**、冷却 | **红 2**：Theory 第 3 行 + `A_job_mismatch_wins_over_a_place_that_is_also_wrong` | 职业 > 地点 有守卫 |
| R2-5 | 次序：**职业、等级**、地点、冷却 | **红 1**：Theory 第 2 行 | 等级 > 职业 有守卫，且被单独隔离 |
| R2-6 | 次序：等级、职业、**冷却、地点** | **红 1**：Theory 第 4 行 | 地点 > 冷却 有守卫，且被单独隔离 |
| R2-7 | 次序：等级、**冷却**、职业、地点 | **红 2**：Theory 第 3、4 行 | 职业 > 冷却 有守卫 |
| R2-8 | 次序：**地点、等级**、职业、冷却 | **红 4**：Theory 第 1、2、3 行 + `A_job_mismatch_wins_over_a_place_that_is_also_wrong` | 等级 > 地点 有守卫 |
| R2-9 | `PlayingCharacter.cs:144`，去掉搬进 `Judge` 的 `rule.CooldownMilliseconds > 0 &&` | **红 1**：`ItemUseJudgementTests.A_declaration_with_no_cooldown_is_never_waiting` | 搬进判决的那条规则**现在有守卫**（第一轮时它落在实例侧且无守卫） |
| R2-10 | `PlayingCharacter.cs:112-114`，`CooldownKeyFor` 去掉分组分支 | **红 3**：`CharacterItemUseTests.Items_sharing_a_cooltime_group_share_the_cooldown`、`UseStackableHandlerTests.Using_one_member_of_a_group_makes_the_others_wait`、`ItemUseJudgementTests.An_item_declaring_a_group_waits_under_the_group` | 与执行者所报一致 |
| R2-11 | 只让**落地一侧**分叉：`ItemUseEffectDomainService.cs:40` → `judgement.CooldownKeyFor(itemId, rule with { CooltimeGroup = 0 })` | **红 1**：`UseStackableHandlerTests.Using_one_member_of_a_group_makes_the_others_wait` | **第一轮 B-1 闭合**。第一轮的同一改坏（E1）当时是全量全绿。这比执行者报的「CS9113 构建失败」是强得多的证据：它证明的是**行为**被守住，而不是编译器恰好拦下了一种写法 |
| R2-12 | 探针（跑完已删），未改任何生产代码：物品 A 组 70 / 3 秒、物品 B 组 70 / 自身 `CooldownMilliseconds = 0`，先起组 70 的冷却，再同时问人物与 `Judge` | **绿** | 人物答 `Allowed`；`Judge` 拿同一条 B 的 rule 加 `IsCoolingDown: true` **也答 `Allowed`**。**第一轮 B-2 实证的那处两半不一致确实消失**，且与基线答案一致 |
| R2-13 | 同一探针文件第二条：用真 `ApplicationModule` 建容器（补 `ICharacterStatDefinitionSource`、`IInitialSkillDefinitionSource`、`IItemUseRuleDefinitionSource` 三个 stub）→ `GetRequiredService<ICharacterDomainService>()` → `Get(60)` | **绿** | 组合根的工厂 lambda 今天**可解析且把 id 原样转发**：`Get(60).Id == 60`，未发布状态答 `CharacterStateUnavailable`。所以 R2-2 暴露的是「无人跑它」，不是「它坏了」 |

#### 复原核对

- `sha256sum -c` 对 572 个源文件全部 `OK`（含被动过的 `PlayingCharacter.cs`、`CharacterDomainService.cs`、
  `ApplicationModule.cs`、`ItemUseEffectDomainService.cs`）。
- 探针文件 `ReviewRound2Probe.cs` 已删除；`git status --porcelain` 与评审开始时逐行一致
  （16 项改动 + 5 项未跟踪）。
- 复原后重跑：`dotnet build` **0 警告 0 错误**；`dotnet test … -m:1` **1171 / 2 / 0**。
- 未提交、未 merge、未清理工作树；未触碰 `Tool/` 与 `.worktrees/legacy-reference/`。

---

### 二、逐条回答本轮点名的四项判断

#### 1. A 级真的闭合了吗？新用例打的是真接缝吗？——**闭合，是真接缝**

- `CharacterRepositoryProductsTests.Build()`（`:82-99`）传给 `CharacterDomainService` 的第四个参数是
  **工厂** `characterId => new PlayingCharacter(characterId, …)`，不是现成实例。因此 `Get` 必须真的运行它，
  并且必须把入参交给它，测试才能过——这正是第一轮点名要打的那一处。
- 三条用例本身的判别力我核过：`OnlyCharacterSixty.FindByCharacter` 只认 60，所以问 60 得 `Allowed`、
  问 61 得 `CharacterStateUnavailable`，两个答案之间**唯一**的差别就是仓库为谁建的实例。
- 我不用执行者的测试也验了一遍：R2-13 的探针经真容器取 `ICharacterDomainService` 并 `Get(60)`，
  `Id == 60`。
- R2-1 独立复现改坏：红 2 条，且**只**红这两条——说明这条接缝此前确实一无所有，现在有且仅有这一处守着。
- `ApplicationModule` 的工厂 lambda **仍然没有任何测试执行过**（R2-2 全量绿）。我判其为 **B 而非 A**，
  理由见 B2-2：第一轮的必修项写的是「必须真正走过 `CharacterDomainService.Get`（**最好**连工厂 lambda
  一起走过）」，必修那一半已满足；SC-B1 / AC-1 的证据条款也已满足；且 R2-13 证明今天的装配是对的。

#### 2. 冷却闸门搬进判决——**行为逐条不变，且不越界**

- **会走到冷却分支的情形穷举**：`rule` 非空且可读、状态可得、等级达标、职业允许、地点允许。此时
  - 基线（被删的 `ItemUsabilityDomainService.cs:55-58`）算的是
    `rule.CooldownMilliseconds > 0 && cooldowns.IsCoolingDown(characterId, CooldownKeyFor(itemId, rule))`；
  - 改后算的是 `rule.CooldownMilliseconds > 0 && candidate.IsCoolingDown`（`PlayingCharacter.cs:144`），
    而 `candidate.IsCoolingDown` 就是同一次 `cooldowns.IsCoolingDown(characterId, judgement.CooldownKeyFor(itemId, rule))`
    的结果（`PlayingCharacter.cs:57-58`）。
  两个布尔式逐字同构，四种输入组合（闸门开/关 × 注册表答是/否）逐一对上。其余五种判定完全未经过这条路径。
- **唯一的差别是求值时机**：`CooldownKeyFor` 与 `IsCoolingDown` 现在**无条件**先算（基线上零冷却声明会短路，
  且等级/职业/地点任一不过也不会走到）。三处纯度我逐个读过：
  - `CharacterItemCooldownDomainService.IsCoolingDown`（`:25-27`）只做一次
    `ConcurrentDictionary.TryGetValue` 加一次 `GetUtcNow()` 比较——不写、不删、不扫；类注释本身就写明
    「过期条目留在原地不清扫」，所以连惰性清理都没有。全仓只有这一个实现
    （`grep ICharacterItemCooldownDomainService` 只命中注册与注入两类位置）。
  - `TimeProvider`：生产是 `TimeProvider.System`（`ApplicationModule.cs:37`），测试是
    `AdvanceableTimeProvider.GetUtcNow()`（`ItemUseFixtures.cs:73`）只 `return now`。都不自增。
  - `ItemUseJudgementService.CooldownKeyFor`（`:109-115`）是纯函数，只比基线多一个 `ThrowIfNull`，
    而调用点 `rule` 已保证非空。
  同理 `Place()`（走 `SessionPresenceRegistry.Find`，`:32-33` 纯读）与 `CharacterJobFamilies.Resolve`
  也变成无条件求值，同样不可观察。**所以提前求值没有副作用，也不改变任何答案。**
- **第一轮实证的两半不一致确实消失**：R2-12 的探针在第一轮的完全相同构造下（组 70、自身零冷却、组正在冷却）
  拿到人物答 `Allowed`、`Judge` 也答 `Allowed`。基线上人物同样答 `Allowed`（旧代码短路），
  所以这处对基线也没有行为变化。R2-9 进一步证明这条搬过去的规则**现在有守卫**——第一轮它无守卫。
- **是否越界：不越界。** C-1 约束的是六种可用性判定的**结果**，那是 `ICharacter.CanUseAsync` 的答案，
  逐条未变（六条既有用例期望值一字未改而全绿，加上上面的穷举）。`IItemUseJudgementService.Judge`
  是本成果新造的接口，没有「改动前」可言。把一条**纯粹比较声明**的规则放进判决，正是 Spec RQ-3
  「判决那半只吃它要比较的东西……吐一个判决」要的形状，也让设计文档新增那节里「两半」的说法**更准**而非更不准。
  契约 Out 的五项一项未碰。

#### 3. 那条六行 `[Theory]`——**覆盖五对，不是六对**

- 规则是四项限制同时施加（等级 50、仅牧师、仅地下城、自带 3 秒冷却），六行逐条放开。逐对核对：

  | 判定对 | 谁守着 | 我用哪次实验证明它会红 |
  |---|---|---|
  | 等级 > 职业 | Theory 第 2 行 | R2-5（只红这一行） |
  | 等级 > 地点 | Theory 第 1、2 行 | R2-8 |
  | **等级 > 冷却** | **Theory 无任何一行覆盖**；只剩 `CharacterItemUseTests.A_permanent_reason_is_reported_ahead_of_one_that_will_expire` | R2-3 |
  | 职业 > 地点 | Theory 第 3 行 + `A_job_mismatch_wins_over_a_place_that_is_also_wrong` | R2-4 |
  | 职业 > 冷却 | Theory 第 3 行 | R2-7 |
  | 地点 > 冷却 | Theory 第 4 行 | R2-6 |

  缺口的机理很直白：第 1、2 行 `coolingDown: false`，第 3~6 行 `level: 50` 已达标——**没有任何一行
  让等级不足与冷却中同时成立**。
- 按要求挑两对故意改坏：**R2-5**（职业提到等级前）只红 Theory 第 2 行；**R2-6**（冷却提到地点前）
  只红 Theory 第 4 行。两次都精确命中且不波及别行，说明这两对是**被单独隔离**的。
- 关于「一行同时放开两个限制」的担心：第 2 行确实同时让职业与地点都不满足，第 3 行同时让地点与冷却都
  不满足。这对**成对次序**是够用的（等级必须同时压过职业与地点；职业必须同时压过地点与冷却），
  R2-4/R2-5/R2-7 各自只红对应的行，隔离性成立。真正的缺口不是隔离，而是上面那一对根本没被造出来。
- Theory 的注释「Two of the six pairs were covered and four were not」（`ItemUseJudgementTests.cs:94-95`）
  因此**在写下它的同一次改动里变成了错的**——那「两对」里的等级 > 冷却，其 Judge 层守卫恰好被同一次改动
  打空。详见 B2-1。

#### 4. 整改自身引入的新问题——一处（B2-1）；无条件查冷却注册表**无副作用**

无条件查注册表的判断依据已在上面第 2 条给全：`IsCoolingDown` 是纯读、全仓单实现、时钟不自增，
`CooldownKeyFor` 是纯函数，`Place()` 与 `Resolve` 同理。R2-9 与 R2-12 从行为侧再次确认。
除 B2-1 外，我在整改带来的代码里没有找到其它缺陷。

---

### 三、发现

#### A 级（阻塞）：**无**

第一轮 A-1 已闭合（R2-1、R2-13）。本轮未发现新的 A 级。

#### B 级（不阻塞）

##### B2-1（**本轮整改引入**）冷却闸门搬进 `Judge` 的同时，把一条按次序命名的用例打成了空转；六行 Theory 覆盖五对

- **文件与行**：`Server/tests/Dfo.Server.Application.Tests/Characters/ItemUseJudgementTests.cs:71-75`
  （`A_permanent_reason_wins_over_one_that_will_expire`）、`:90-98`（Theory 的注释）、`:99-124`（Theory）；
  `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:144`（新闸门）。
- **机理**：那条 Fact 用的是 `ItemUseRules.RequiringLevel(50)`，其展开是
  `Potion(cooldownMilliseconds: 0) with { MinimumLevel = 50 }`（`ItemUseFixtures.cs:52-53`），
  `CooldownMilliseconds` 为 **0**。闸门搬进 `Judge` 之后，`rule.CooldownMilliseconds > 0 && candidate.IsCoolingDown`
  对这条 rule 恒为 false，于是它传的 `coolingDown: true` 再也不能改变任何答案——这条用例**无论 `Judge` 的
  次序怎么改都不会红**。
- **实证**：R2-3 把冷却检查提到 `Judge` 的最前面，它**仍然绿**；红的是 Theory 第 3、4 行与
  `CharacterItemUseTests.A_permanent_reason_is_reported_ahead_of_one_that_will_expire`。
- **后果**：等级 > 冷却这一对在 `ItemUseJudgementTests`（本成果新建的、专门用来钉次序的那份文件）里
  **没有守卫**，只靠人物层那条撑着；而 Theory 的注释又声称那一对「已被覆盖」，读起来像是钉住了，其实没有。
- **分级理由：B 而非 A**。那一对整体仍有守卫且我实测会红（R2-3）；契约 SC-B4 明确要求的那条
  （等级不足与冷却中同时成立时仍报等级不足）成立；C-3 的代码次序一字未改；SC-B7 要的是「对每一处**保证**
  各演示一次会红」，这处保证演示得出来。它是**证据质量**问题，不是行为问题。
- **修法（一处参数即可，建议在封存前顺手做掉）**：把那条 Fact 的 rule 换成带自身冷却的，例如
  `ItemUseRules.RequiringLevel(50) with { CooldownMilliseconds = 3_000 }`；或给 Theory 补第七行
  `[InlineData(10, QuestJobRequirement.Priest, ItemUsePlace.Dungeon, true, ItemUsability.LevelTooLow)]`。
  两者任一都能让 R2-3 在 Judge 层也变红，并让 `:94-95` 那句注释重新为真。

##### B2-2 `ApplicationModule` 的工厂 lambda 仍无任何测试执行过

- **文件与行**：`Server/src/Dfo.Server.Application/ApplicationModule.cs:88-96`。
- **实证**：R2-2 把整个 lambda 换成抛异常，**全量 1171/2/0 全绿**。
- **但今天没有缺陷**：R2-13 的探针用真 `ApplicationModule` 建容器，六个依赖全部可解析，
  `Get(60).Id == 60`，未发布状态答 `CharacterStateUnavailable`。
- **分级理由：由第一轮 A-1 的一部分降为 B**。第一轮的必修项原文是「必须真正走过
  `CharacterDomainService.Get`（**最好**连 `ApplicationModule` 的工厂 lambda 一起走过）」——必修那一半
  已满足且可证伪；SC-B1 / AC-1 的证据条款（「能指出交出它的方法」+「一条用例证明未被选中时的答案」）
  也已满足并超额。组合根的装配在全仓其它地方同样没有逐行用例，这不是本成果新引入的空白。
- **顺带一处时机变化（不是缺陷，只作记档）**：基线上 `IItemUseRuleDefinitionSource` 等是
  `ItemUsabilityDomainService` 的构造参数，随该服务一次性解析；现在它们在 lambda 体内 `GetRequiredService`，
  于是「少一处注册」从「解析该服务时炸」变成了「第一次有玩家用道具时炸」。
  R2-13 那条约 20 行的冒烟用例可以同时关掉这一条和上一条。

##### B2-3 第一轮 B-6 只处理了三份工件中的一份，且处理的不是含自相矛盾的那一份

- `attempts/spec-002-the-character-instance/spec.md:12`：Outcome 段仍**逐字**写着
  「它在选人时诞生、退出时消失，与背包同处同时」——与同一份文件 `:24` 的 RQ-1
  「它**不需要一套自己的生命周期**」直接矛盾，也与契约 revision 2 的修订说明直接矛盾。
- 同文件 frontmatter `:5` 仍是 `contract_revision: 1`；`sdd-execution.yaml:5` 仍是 `revision: 1`。
  契约是 **revision 2**。
- 已改对的只有 `multi-spec-map.md` 的 Spec Attempts 第 2 行（现写「按需交出（无注册故无生命周期，
  见契约 revision 2 与 R-4 的改写）」）。
- **执行者的自查在这一条上不准确**：自查称「契约正文与父控制器记录的成果描述已改；`spec.md` 的 RQ-1
  本轮之前已改过」。矛盾**从来不在 RQ-1**——第一轮 B-6 点的就是 Outcome 段与 frontmatter，两处原样未动。
  请父控制器不要按「已处理」采信。
- **分级**：仍为 B（不影响代码，且 SC-10 的权威——设计文档——是对的）。但 `spec.md` 是下游
  `verification` 阶段要读的验收目标，一份自相矛盾的验收目标会让「验的是哪一个形状」变成开放问题，
  **建议在封存前处理**。

##### B2-4 第一轮其余 B 级逐条复核：无一应升级为阻塞

| 第一轮编号 | 本轮现状 | 是否应升级为 A |
|---|---|---|
| B-1 用与判两侧冷却键无守卫 | **已闭合**：R2-11 只让落地一侧分叉，`Using_one_member_of_a_group_makes_the_others_wait` 会红 | — |
| B-2 `Judge` 与人物对同一条 rule 不同答 | **已闭合**：闸门已进 `Judge`（`PlayingCharacter.cs:144`），R2-12 探针两侧同答 `Allowed`；R2-9 证明它有守卫 | — |
| B-3 次序只有两对有守卫 | **基本闭合**：六对现在全部可红（R2-3..R2-8）。Judge 层仍缺一对，见 B2-1 | 否 |
| B-4 身份解析第一步（会话目录）无人构造 | 未动。`PlayingCharacter.cs:69-78` 与被删的 `FindCharacter` 逐字相同，是继承来的缺口而非本轮回归；契约未要求两步各有用例 | 否 |
| B-5 `NotAUsableItem` 先于 `CharacterStateUnavailable` 无守卫 | 未动。`CanUseAsync` 里两个判断的先后与被删文件一致，行为未变 | 否 |
| B-6 三份工件仍陈述被推翻的形状 | 只处理了三分之一，且不是含矛盾的那一份，见 B2-3 | 否（但须在封存前处理） |
| B-7 R-6 的 nuance 少说两个参数 | 未动。`ICharacterEntryRules.cs:94-101` 的 `CanCarryOut` 确实同时还收 `int accountId` 与 `int characterId`，而 `DomainInstanceDesign.md:203` 仍只举 `recordedAccountId` | 否 |
| B-8 新类型的落位与文件名残留 | 未动。`IItemUsabilityDomainService.cs` 里已无该接口，只剩 `ItemUsability` 枚举，文件名指向不存在的类型 | 否 |
| B-9 `EquipmentEligibilityCharacterFactsTests` 用 `null!` | 未动：`:32` 仍是 `null!`，只在上方加了两行注释；同一轮另外三处 stub 用的是带一句话的 `NotSupportedException` | 否 |

判断依据统一说明：B-4、B-5、B-7、B-8、B-9 都不是行为缺陷，也不对应任何一条 SC 或 AC 的必需证据，
且 B-4/B-5 是从被删的旧类原样继承下来的既有缺口而非本轮回归。B-6/B2-3 关系到下游阶段读到什么，
但同样不改变代码的正确性。故均维持 B。

---

### 四、SC 与 AC 逐条核对

#### 契约 Success Conditions

| ID | 判定 | 依据 |
|---|---|---|
| SC-B1 | **满足**（第一轮为部分满足） | 类型 `ICharacter`（`ICharacter.cs:23`）/ `PlayingCharacter`（`PlayingCharacter.cs:21`）与交出它的方法 `ICharacterServices.cs:71` / `CharacterDomainService.cs:28` 都能指出；「未被选中时答 `CharacterStateUnavailable`」有用例；**「由仓库交出」现在有一条真走 `Get`（且经工厂）的用例，并且可证伪**——R2-1 红 2 条 |
| SC-B2 | 满足 | `UseStackableCommandHandler` 的构造参数是 `ICharacterDomainService`（`UseStackableSlice.cs:224`），提问是 `characters.Get(command.CharacterId).CanUseAsync(...)`（`:252-253`）；全仓只有这一处生产调用方 |
| SC-B3 | 满足 | 我重新清点：`Server/src/Dfo.Server.Abstractions` 下六个含 `RuleService` 的文件里 `CharacterId` / `AccountId` 命中数为 **0**（`IGoldLimitRuleService`、`IExpertSkillContractRuleService`、`IDungeonEffectRuleService`、`IItemArrivalRuleService`、`IDailyRefillRuleService`、`IItemSlotRuleService`、`IDungeonLayoutRuleService`、`IEntryPlacementRuleService`、`IWorldContentEntryRuleService`）；本轮新增的 `IItemUseJudgementService.CooldownKeyFor(int, ItemUseRule)` / `.Judge(ItemUseRule, ItemUseCandidate)` 同样不带身份 |
| SC-B4 | 满足 | 七条用例俱在，`git diff -M` 证明六条既有期望值一字未改；契约点名的那条次序用例 `CharacterItemUseTests.A_permanent_reason_is_reported_ahead_of_one_that_will_expire` 在，R2-3 实测会红。附注 B2-1 |
| SC-B5 | 满足 | 独立复核：`dotnet build` **0 警告 0 错误**；`dotnet test Server/86JP.Server.slnx -m:1` = **1171 / 2 / 0**。相对第一轮 1160 净增 11，且逐条对得上：`CharacterRepositoryProductsTests` 3 条 + `ItemUseJudgementTests` 新增 7 条（6 行 Theory + `A_declaration_with_no_cooldown_is_never_waiting`）+ `UseStackableHandlerTests` 1 条 |
| SC-B6 | 满足 | `DomainInstanceDesign.md` 中「Whether the character instance is also the runtime session state」bullet 已删；`:318` 起的 `## Settled by the character-side implementation` 写明结论、理由、所依赖的前提与何时重开。本轮未改动，第一轮结论不受影响 |
| SC-B7 | **满足**（第一轮为不满足） | 本成果新建立的每一处保证我都独立演示过会红：**由仓库交出** R2-1；**判决可脱离角色单独调用** R2-3..R2-8（红的全是不构造任何角色的判决层用例）；**判定次序**六对全部可红（R2-3/R2-4/R2-5/R2-6/R2-7/R2-8）；**「声明本身没有冷却就不可能在等」** R2-9；**用与判两侧冷却键一致** R2-11。附注 B2-1 |

#### Spec Acceptance

| ID | 判定 | 依据 |
|---|---|---|
| AC-1 | **满足**（第一轮为部分满足） | 同 SC-B1。「能指出类型与交出它的方法」✓；「一条用例证明角色未被选中时的答案与今天一致」✓；且现在**经由仓库**的那一半也有用例并可证伪 |
| AC-2 | 满足 | 处理器不再持有可用性服务；六条既有用例期望值一字不改而全绿（我独立跑过，并逐条核对了 `git diff -M`） |
| AC-3 | 满足 | 清点见 SC-B3；`ItemUseJudgementTests` 全部 16 条不构造任何角色、不建会话、不碰目录与库。第一轮 B-2 记的「判决半不是规则全集」已由本轮整改消除（R2-12 探针两侧同答） |
| AC-4 | 满足 | 七种结果逐条可构造；六对次序全部实测可红（R2-3..R2-8） |
| AC-5 | 满足 | `PlayingCharacter.Place()`（`:93-96`）与被删的 `PlaceOf` 逐字相同，默认仍是村庄；守卫用例在 |
| AC-6 | 满足 | 同 SC-B6 |
| AC-7 | 满足 | 同 SC-B5 |
| AC-8 | **满足**（第一轮为不满足） | AC-1 的演示现在打的是「由仓库交出」本身（R2-1），不再只是「未被选中时的答案」；AC-3、AC-4、AC-5 的演示我也各自独立复现过 |

#### 约束核对

| 约束 | 判定 | 依据 |
|---|---|---|
| C-1 六种判定结果逐条不变 | 未违反 | 第二节第 2 条的路径穷举 + 六条期望值未改 + 全量绿 |
| C-2 三份整记录表征用例期望值不得改 | 未违反 | `git diff --stat -- Server/tests/Dfo.Server.Persistence.Tests/` 输出为空 |
| C-3 判定先后次序不得改变 | 未违反 | 四条比较的相对次序与被删文件逐条一致（`PlayingCharacter.cs:126 / :131 / :136 / :144`） |
| C-4 不另开与查询并行的第二套规则 | 未违反 | 仍是一条查询，落地仍在 `ItemUseEffectDomainService` |
| C-5 实例不缓存状态，只持身份与依赖 | 未违反 | `PlayingCharacter` 只持 `characterId` 与六个依赖引用（`:21-28`），每次提问重读目录；`CharacterRepositoryProductsTests.Two_asks_for_one_character_answer_the_same` 把这条钉住了 |
| 不得改判定所读的权威来源 | 未违反 | `PlayingCharacter.cs:69-78` 与被删的 `FindCharacter` 逐字相同：`ISelectedSessionDirectory` → `ISelectedSessionRuntimeDirectory` |
| 六条原有可用性用例期望值不得改 | 未违反 | `git diff -M` 逐条核对 |
| C# 文件 CRLF、无 BOM UTF-8 | 未违反 | 7 个新增/改动文件 100% CRLF、无 BOM |
| net10.0、`TreatWarningsAsErrors` | 未违反 | 构建 0 警告 0 错误 |
| 不得改动 `Tool/`、`.worktrees/legacy-reference/` | 未违反 | `git status` 无相关条目 |
| 不提交、不 merge、不清理工作树 | 未违反（评审侧） | 572 个源文件 sha256 全 `OK`；探针已删；`git status` 与评审开始一致 |

---

### 五、通过，附两条建议

本轮**无阻塞发现**，判 `passed`。以下两条是 B 级，不阻塞本轮通过，但都是一处改动的量，
建议在父控制器做接受提交之前顺手处理，否则请明确记档：

1. **B2-1**：把 `ItemUseJudgementTests.A_permanent_reason_wins_over_one_that_will_expire` 的 rule 换成
   带自身冷却的（或给 Theory 补第七行），让等级 > 冷却这一对在判决层也真的钉住，并让 `:94-95`
   那句「Two of the six pairs were covered」重新为真。
2. **B2-3**：`spec.md:12` 的 Outcome 段与 `spec.md:5` / `sdd-execution.yaml:5` 的 `revision: 1`。
   下游 `verification` 阶段读的就是这份 spec，一份与自己的 RQ-1 直接打架的验收目标不该被封存进记录。
