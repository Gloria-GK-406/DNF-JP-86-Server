# TASK-27 — F-122 通知登记结果 Registered 命名闭合

## Task Unit Contract

- Revision: 1（本契约与详细计划的共同修订）。
- Outcome and references: 将 `NotificationOutcome.Delivered` 更名为 `Registered`，闭合全部实际消费点、同源队伍结果和当前实现说明；含义仍是接受登记，连接已不在时丢弃也属于该结果，不承诺交给传输或客户端收到。消费 Spec B3 的 RQ-013、AC-008，以及相关 AC-009/011、Plan P4 T-004/F-122；不新增可见变化，不迁移任何协议 CMD，不改变主结算、重放、来源许可或投递机制。
- Owned boundary: §2 精确 26 个现存产品／测试／文档路径、本 Task、`verification/evidence/task-27-inspection/` 与 `verification/logs/task-27-*` 新证据。基准、两份 canonical migration-ledger、其它历史制品均只读。没有产品新增／删除路径；唯一预期新增用例为同 provider 的登记先于物理发送观察。
- Required checks: 原名相关 focused 基线；新增观察先在原名通过；枚举值及默认值、所有旧分支和业务表达式的有界命名归一对照；真实 notification/GM/quota/party 原强用例；最终同产品 freeze 的 Release `--no-incremental` 0 警告 0 错误、八项目 fresh 无 filter full、9013 原库存逐 display/status/multiplicity 追踪、DFO 原 74/7、Golden15/Characterization10、source/runtime/pure family/三个许可 owner 不变、current strict 与原八 selftest、完整 NUL 路径分区／nonowned SHA／manifest／行尾及新增文件 whitespace 检查。
- Dependencies: TASK-26 R2 已由独立 Review R2 approved、主控完整消费并提交于 `90b88aae3b07f0ec3dbb25eae22e2463ac90344f`；本 Task 不消费其它未提交单元。
- Result review: required — 共享结果误命名、漏消费或错误推进配额水位会使 GM 返回值、精确收件人或同周期刷新改变。改名编译通过不能独自发现时序变化，必须由 fresh 独立结果快审同时核源码差分、旧强用例、登记前发送观察与全量证据；主控 readiness 后 fresh producer 实施，不增加自动 Task plan review gate。

## Detailed Plan

### 1. 固定起点与保留语义

唯一工作目录 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`。实施前重新确认 HEAD 为上述提交、index 空、除本 Task 计划外无未归属变化。本文 B 指 `.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd`，E 指 `B/verification/evidence/task-27-inspection`。不改 root/child TaskContract、B3/P4、baseline、approved gates、A seal、routing 或其它 Task。

现有 `INotificationBus.cs` 顺序是 `Delivered=0, Declined=1, Gone=2, Failed=3`，默认值是首项。只将首项声明改为 Registered，底层类型、隐式数值顺序及其它三项不变；不保留 Delivered alias，不更名 `NotificationAttempt`、`NotificationDelivery`、`With` 或总线 API。删除该首项注释中“保留旧名直至 T-008”的过渡叙述，直接说明已组装／编码并接受登记、缺连接时可丢弃、实际 sender 在 job 结束后处理。

`NotificationBus.AttemptAsync` 原顺序逐项保留：检查取消→精确存活校验→Compose→空则 Declined→再检查取消→按原 context 查找 SendBatch 或原 Debug 丢弃→返回首项；调用者取消原样上抛，其余异常原 Warning＋Failed。不存在把返回点挪到物理发送结束、增加 await／重试／超时、改变 outbox Flush/Commit/Rollback、连接关闭或登记失败处理的授权。

GM reminder 仍对 distinct accounts 逐个取 session；session 缺失／Gone／Declined 表示 offline；Failed 累计后原样抛出；全部接受登记即 attempted，之后传输失败不把它改成 offline。`GmCeraDomainService.cs` 只改 reminder 内 enum 引用；余额、SQL、audit、授权、旧 catch／StorageFailure、LegacyNonRequest footprint、Cera 协议与 P3 四 opening 恢复范围全部只读，留后续单元。

七个 daily refresh 仍为 Altar、Heras、Tournament、Dimension、GoldSilverHorn、ReviveCoin、Mirror。保留原周期／时区、exact session、水位 prune、登记前后 IsInWorld、失败不推进水位、同周期传输失败后不重复登记。非 Mirror 六项是原 DateTimeOffset period；Mirror 是 UTC+8 local midnight DateOnly。不要改成新的 fingerprint 或统一去重模型。Premium 不直接消费该 enum，完整权益 fingerprint／前次激活 flag／清零顺序只读，不能趁改名实施 F-07。

### 2. 精确物理白名单与检索闭合

以下路径相对 worktree 根。表中只准改 enum 首项、该项的真实引用／同源登记局部名称、对应当前说明，以及 §4 单个观察用例所需窄 helper；不能清理整文件格式或其它旧注释。

| 现存产品路径（12） | 允许的变化 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/Notifications/INotificationBus.cs` | enum 首项及其登记语义注释 |
| `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs` | 末端成功 return 的 enum 名 |
| `Server/src/Dfo.Server.Application/Administration/GmCeraDomainService.cs` | reminder 的成功 enum 判定 |
| `Server/src/Dfo.Server.Protocol/Game/Party/PartyTownProjectionDelivery.cs` | enum 消费；`PartyExactNotificationDeliveryResult.Delivered` 同源属性改 Registered；其登记集合局部名有界同步 |
| `Server/src/Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs` | 两个 Party result 属性消费及同源局部名同步 |
| `Server/src/Dfo.Server.Protocol/Game/Altar/AltarQuotaDailyRefreshService.cs` | enum 及仅代表成功登记的局部水位名 |
| `Server/src/Dfo.Server.Protocol/Game/Tournament/HerasQuotaDailyRefreshService.cs` | 同上 |
| `Server/src/Dfo.Server.Protocol/Game/Tournament/TournamentQuotaDailyRefreshService.cs` | 同上 |
| `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/DimensionQuotaDailyRefreshService.cs` | 同上 |
| `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/GoldSilverHornQuotaDailyRefreshService.cs` | 同上 |
| `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/ReviveCoinQuotaDailyRefreshService.cs` | 同上 |
| `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/UsageQuotaProjection.cs` | Mirror 的 enum 判定及同源局部水位名；其它投影行为不变 |

| 现存测试／support 路径（12） | 允许的变化 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationDeliveryTests.cs` | enum 引用与单个新增登记前发送观察 |
| `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationWorld.cs` | 仅为该观察提供真实同 provider、同 publishing job 内的窄测试入口 |
| `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationInWorldTests.cs` | enum 引用 |
| `Server/tests/Dfo.Server.Application.Tests/Accounts/ClientSettingsBlobTests.cs` | stub 成功 enum 引用 |
| `Server/tests/Dfo.Server.Application.Tests/UsageQuotas/UsageQuotaTests.cs` | stub 成功 enum 引用 |
| `Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs` | stub 成功 enum 引用 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyTownProjectionDeliveryTests.cs` | stub／成功判定 enum 引用 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyNotificationTests.cs` | 同上 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs` | stub 成功 enum 引用 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonLoadingProjectionDeliveryTests.cs` | stub 成功 enum 引用 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/UsageQuotaProjectionTests.cs` | enum 引用；保所有原 theory 参数／断言 |
| `Server/tests/Support/XoDelivery.cs` | `XoProjections` 登记结果的 enum 引用；真实 sender/transport/observer 实现不变 |

| 现存文档路径（2） | 允许的变化 |
|---|---|
| `AGENTS.md` | 当前通知结果说明由“名字没跟上”改为 Registered 的事实定义；不写任务过程、不链接 `.rks-agent/` |
| `protocol/game/server-to-client/NOTI-0x0009.md` | 仅人工区当前服务端行为中 town publication 的那个 bullet：Registered 表示接受登记、按登记顺序继续 read-repair，不承诺物理收到。机器区、客户端历史、抓包／截图事实、证据等级及其余正文逐 byte 保持 |

规划时直接 enum／声明命中是 11 产品＋11 测试路径；额外 Town 间接消费者＋NotificationWorld helper＋2 文档，因此精确白名单 12＋12＋2＝26 个现存文件，无产品新增／删除。全限定 enum 用法位于六个 refresh，必须同步，不能仅替换 using 导入形式。

实施前保存完整 `NotificationOutcome` 检索、声明／别名／全限定／`using static`／反射／`nameof`／字符串与序列化检索的 raw。当前 Server/Tool/Web 源码扫描的 27 个 NotificationOutcome 文件中，另五个仅使用 Failed/Gone（DungeonRoomMechanismNotificationSubscriber、AltarProjection、DomainEventEntryTests、RepairItemProtocolCommandFlowTests、CrystalContractProtocolCommandFlowTests），保持只读。当前没有发现针对 NotificationOutcome 的 enum 反射、Delivered 字符串映射或生产 JSON 字符串序列化契约；producer 应复核实际 `Outcome` 流经的日志／JSON 投影，不把普通变量同名当证据。如发现遗漏的新真实消费者，记录准确 path／调用链返主控修订白名单后再写，不能自行全仓替换。

Party 只改由成功 enum 汇集的 Registered 集合名及其两处消费：distinct、pending 初值、MaximumAttempts=2、只重试 Failed、集合成员／顺序、HasFailures、town 后接 party 的调用顺序原样保留。其它 `OutboundOutcome.Delivered`、`DomainEventDeliveryStatus.Delivered`、实际 transport delivered、CardReward Delivered／reward 标记等保持。不改普通英语词、可靠事件状态、历史 `.rks-agent` 制品、参照 ServerS4A21 文档或 protocol 历史证据。Docs/README.md 当前不存在，不据想象补写；AGENTS 已明确本仓现状与参照文档的分类规则。

### 3. 产品前封存与原工具对照

用 NUL 分隔的 `git ls-files -z` 获取完整 tracked 集合，包括中文路径；26 个 owned 必须已存在且 current bytes＝HEAD。保存每项 bytes／SHA／行尾及原测试方法声明、Theory/InlineData/MemberData 参数。tracked 精确分为原存在 owned 与全部 nonowned，互斥且并集完整；nonowned 每项也先验 current＝HEAD。两 actual canonical 键是 `B/verification/migration-ledger.json` 与 `B/verification/migration-ledger.md`，必须真实进入 nonowned map，不凭后缀漏掉、不默认排除；本 Task 两者从头至尾逐 byte 不变。Task 本身与自有新增证据另记 ownership，不吞入产品白名单。

先在 E 封存最小原源／枚举顺序／所有 consumer 分支和原强测试声明，以及 HEAD、branch、index、UTC、真实命令、原退出码。用原 `B/verification/probe-tools/migration-ledger.py capture --repo <worktree> --output <E/capture-before-01>` 捕获 source/runtime，再按 Task26 `family-export.cs` 的原纯注册方法捕 pure family／registration exits；新 driver 只能写 E，原工具只读。不以人手算数替代 capture，不起 Host／数据库／游戏。before／after 的完整 rows、source declarations、logical→handler→CMD／route、execution kind、bindings、三个原 registration-exit permit 全部 owner 精确相同；当前参考是 173 routes／201 commands，若不符先定位而非改 expected 求过。本 Task 不改变注册、任何路由或三许可所有权。

canonical 当前没有 F-122 行，不能伪造 protection/source/transition ID 或顺带关闭未迁协议。保原 counts 和全部行；以 read-only `validate` 的 strict 语义检查及原 `selftest` 八坏例对 current baseline/ledger 验证一次即可，输出只存 E。两个 ledger 和验证器均未改，不再制造八份物理坏副本海量证据；这不取消原已接受八类坏例或其它 gate。F-122 的本单元实现／消费／证据映射落 Result 与 E 的 coverage 说明，不篡改两份 canonical。

### 4. 原行为守护与有界改名

先对未改产品的相关现有测试作 Release focused 基线，保 detailed console、原并行／5m deadline／Skip。选择 App 的 NotificationDeliveryTests、NotificationInWorldTests、ClientSettingsBlobTests、UsageQuotaTests、SelectCharacterLifecycleTests；Protocol 的 PartyNotificationTests、PartyTownProjectionDeliveryTests、TownProjectionHandlerTests、DungeonLoadingProjectionDeliveryTests、UsageQuotaProjectionTests，以及只读 GmCeraReminderTests、TownAreaProjectionDeliveryTests。按程序集／完整类名 OR filter 记录实际执行 display/status/multiplicity，不能用错误 filter 的零用例通过替代。

现有真实强证据已覆盖：NotificationDeliveryTests 的物理失败仍在成功摘要、单收件人 compose／encoder 失败隔离、整 batch 编码原子性、取消／Gone／Declined；GmCeraReminderTests 的 attempted、offline、Gone/Declined、compose error、transport failure；UsageQuotaProjectionTests 的七 family 各自“传输失败仍记录周期”和“组装失败不记录、同周期重试”共 14 个 theory case；另有原周期／snapshot／stale／cache 及 Party retry、town 顺序用例。保全部原方法目的、参数、断言强度和实际驱动，不为了名字把 counts-as-sent／physical delivery 的强意图改弱。

缺口仅为通知结果返回时仍在 publishing job body、尚无物理写的直接观察。新增一个 Fact，沿 NotificationWorld 原 provider 的 GameplayExecutor→真实 INotificationBus→真实 sender registration/outbox 路径；只增加 test-only 窄入口让该 Fact 在 `PublishAsync` 返回后、当前 job 结束前观察结果。此时断言已接受结果，且原 WorldDelivery 的提交记录和实际 Sent 均为空；job 结束并用原 SettledAsync 排空后，断言原精确收件人／消息实际提交并发送一次。不能自行造 NotificationDelivery、替换总线、提前 flush、用第二 provider，不能改生产 API 或隐式 request。helper 不改变原 PublishTo/Project 等调用路径及原 finally 排空；不加另一个泛化测试平台。该观察先用旧成员 Delivered 在未改产品上运行并通过，保 raw，证明是已有语义；再把测试及产品同步改 Registered，重跑同 Fact/相关 focused。纯符号搬迁的编译失败不是行为 RED，不造 TDD 假 RED 或无价值 mutation。预计只新增此一个 case，最终以实际结果为准。

按白名单一次闭合 enum、全部实际消费、Party 间接结果及文档。原局部 `delivered` 若只指接受登记，可精确改为 `registered`；不随意重命名其它方法／成员／日志文案／测试方法。以每个 owned 源的命名映射归一／语法或 token 对照证明，除 enum 首项、登记专属标识符、准确注释及单个测试/helper 外，业务表达式、条件、常量、调用及 await 顺序相同。特别输出枚举底层／四值／default 对照、GM 的原三路结果、七水位更新谓词和 Party 两轮集合顺序；不能把任意 token 替换器当作“语义等价”的独立证据。文档两处按允许区间 diff，保其余 bytes 与行尾。

### 5. 最终同 freeze 验证与完整库存

产品／测试／文档编辑完成后冻结全部 26 源及自有新增测试／driver 当前 bytes/SHA；如后续修源，重新 freeze 并仅按实际受影响检查重跑，不冒称旧输出来自新源。由于共享契约、bus 返回、跨项目消费和公共 linked support 变化，最终必须完整 Release solution build 后重新运行八项目无 filter full，不能复用 Task26 其余七项目作为本 Task 的 fresh 结果：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

八 project 是 Dfo.Server.Application.Tests、Dfo.Server.Protocol.Tests、Dfo.Server.Networking.Tests、Dfo.Server.Host.Tests、Dfo.Server.HttpApi.Tests、Dfo.Server.Infrastructure.Tests、Dfo.Server.Persistence.Tests、Dfo.Pvf.Tests。沿原 logging driver 创建 Task27 自有运行前缀，保原实际命令／cwd／HEAD／UTC／退出码／资源／raw detailed 输出。native 使用 `require_escalated` 避免 sandbox FSEvents hang；不 TERM 活跃 native 或父 runner，不覆盖输出，不改 port／并行／timeouts／Skip 或测试基础设施求绿。

Task26 `E26/formal-summary-r2.json`（E26=`B/verification/evidence/task-26-inspection`）是本次原库存的精确选取来源：App02 5236＝5218 pass＋2 exact Dimension fail＋16 skip；Protocol01 1778、Networking01 199、Host02 203、HttpApi01 57、Infrastructure01 976＝974 pass＋2 skip、Persistence01 484、Pvf01 80；总和 9013＝8993 pass＋2 fail＋18 skip。App02 是 R2 fresh，其余是 R1 未受影响源的选择，本 Task 不把这些旧轮次说成旧 freeze02 八 fresh。新的八 full 按每个完整 display／status／multiplicity 与该选取原 raw 建 Counter，逐 case N/D/rename/status 闭合，不能只比总和。保所有原 9013，预期 N=1、D=0、rename=0，从而 9014＝8994 pass＋原 2 fail＋18 skip；必要名称差异逐 case 对照原参数与目的，不能抹平原失败或放宽断言。

仅允许 root baseline 登记的两条 exact Dimension 身份／形状作为旧失败；从 Task26 原 raw 提取并附名、参数、失败原因，不拿“Dim”模糊替代。Task26 Host01 的 ComponentsOpenOnlyTheirConfiguredPortsAndReleaseThemOnStop(http: False, game: True) 在 channel port line39 的失败与邻近 bind SocketException48，未证明唯一根因，且同源 Host02 全 203 pass；它不是新豁免。新失败保存 raw 定位，有依据时可同 source freeze 复跑相应完整项目，保所有轮次和原局限；不能调端口／timeout／Skip，把邻近日志当唯一根因，或只择成功行拼过。

每轮 DFO 使用独立目录，原 7 actual control 必须真实出现在对应七程序集的本次 full，Pvf 无 control。沿原 violations.py CLI 及原 expected class／count／assemblies 核 74 records／7 controls、outside/exe/orphan=0、新 intentional=0；不得减少类／expected、双算 controls 或混 focused 与 full。若需要采用不同 round 的已完整结果，只能新建最终 bundle，逐 bytes 复制每个已选 full 的原 JSONL，记录每个来源 round／路径／bytes／SHA，保全部原件，再执行原 CLI；不改 expected 求过。exe0 仍只是观察边界。Golden15 逐 byte 同 current HEAD 与 `f01dc226`，Characterization10 逐 display 全 pass／0 skip；CMD/NOTI、线级／codec 原断言保。套件耗时只作观察，不代表单条 CMD≤2 秒验收，该测量仍属 T-012。

同 freeze 再跑原 capture／pure family 方法并与 before 完整比对，证明没有命令注册／route／三 permit owner 改动。复查 live 源零旧 `NotificationOutcome.Delivered`、零 Delivered alias 和旧全限定／别名消费；白名单的 Party 登记结果不再冒称物理 delivered。历史 Task／非本 enum 的可靠或 transport Delivered 不追零。

### 6. 证据收口与交独立快审

E 保存原 focused／旧名观察、新 focused／新名观察、完整源码／检索对照、source/runtime/pure family、enum／业务表达式差分、formal 原输出的索引、N-D/逐 case、DFO、Golden/Char、ledger 只读检查、coverage 与明确局限。日志只用 task-27-* 新名；原工具、Task26 所有源证据、baseline／Spec／Plan／A seal不改。

最终复核 tracked＝owned＋nonowned 仍完整；所有 nonowned 当前 SHA＝before＝HEAD，包括两个 canonical ledger；index 仍空。Git NUL changed＋untracked 路径与精确 26 源、Task27、自有证据／日志 manifest 闭合。manifest 每项 bytes/SHA，排自身 hash 并注明；Task 使用精确冻结 prefix/hash 避循环，不把未 append Result 的旧 Task SHA冒最终。先把实际新文件都列入 freeze／whitespace检查，未 tracked 文件不能靠普通 git diff --check 漏过；用 no-index/等效逐文件方式检查它们，保 CRLF 正确 `cr-at-eol`。Task26 已接受的只读 InventoryMoveDomainService 末尾空行不在本 Task diff，不能顺便修；无权放宽本次 authored whitespace。文档机器区／历史区与白名单外 bytes检查同样入 manifest 证明。

producer 完成后向本 Task append literal `Outcome: done` 的 Result，给出实际命令／退出码、精确源 freeze／manifest／Task bytes/SHA、原库存与新增观察逐 case、F-122 完成证据和未做局限，停止 writer/native，交 fresh required 独立结果快审；不能自批、先做 Task28 或把本 Task 当成 Spec002 全完。主控完整消费后才进行本地 checkpoint。全程中文，不 stage／commit／push／PR／merge／remote／版本／tag／部署／实机游戏／cleanup；后续分支收尾仍须 Astra 审核通过且用户明确要求的既定授权。

## Result R1

- Outcome: done
- Producer/identity: fresh bounded TASK-27 producer；唯一 worktree `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`，HEAD `90b88aae3b07f0ec3dbb25eae22e2463ac90344f`；Result review 仍为 required，待主控 fresh 独立快审与本地 checkpoint，本文不自批。
- 交付：26 个既有路径内闭合 `NotificationOutcome.Registered`、Party 同源结果与 Town 两消费、七 quota 登记局部名称及两处当前文档；无 Delivered alias，四数值 0/1/2/3 和 default0 保持。bus/GM/七水位/Party 两轮仅重试 Failed 的原分支、await、物理失败语义全部不变。`semantic-equivalence.json`、`owned-diff.patch`、`coverage.md` 保存逐 byte 有界对照与逐分支说明。
- 旧名证据：原 focused Application64/Protocol106 全通过；一个真实同 provider publishing job 内的观察先在旧 Delivered 名称下通过 1 case（`task-27-observation-old-02`）。首轮 helper 缺 RunAsync 必需取消参数的 CS1501 留 `old-01`，普通修正后通过，不算行为 RED。新名 focused Application65/Protocol106 全通过；GM6 与七 quota14 原强 case/参数/意图保留。仅新增这一观察，结束排空后精确 recipient 的一批两消息按序提交/发送；没有 fake result/bus、第二 provider 或提前 flush。
- 同 freeze 正式核验：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental` exit0、0 warning、0 error；八项目 fresh 无 filter full、原并行/5m/Skip 全保并全部自然结束。Application5237、Protocol1778、Networking199、Host203、HttpApi57、Infrastructure976、Persistence484、Pvf80；逐 display/status/multiplicity 原9013全部保留，新9014=8994 pass+原exact2 Dimension fail+原18 skip，N1/D0/rename0/statusChanges0。Host 本轮完整203 pass且无重跑，不增旧 Host 豁免。详见 `formal-summary-01.json`、八个 `inventory-*.json` 和 `verification/logs/task-27-formal-*.log/json`。
- 其它 checks：原 violations.py CLI/expected class/count/assemblies exit0，74 records/7 actual controls、outside/exe/orphan0、新 intentional0；Golden15 逐 byte 同 HEAD/f01dc226，Characterization10 pass/0skip。原 capture source/runtime/payload/crosscheck 与 pure family 完整 before/after 逐 byte 一致：173 routes、201 commands、35 Protocol+1 Internal+165 LegacyFine、137 Tx+51 Direct+9 ReadOnly+4 Workflow、46 message ProtocolCommand+127 LegacyQueued、14 telemetry、三种原 registration exit 的全部 owner 不变。对应 `registration-equivalence.json`、`golden-15.json` 和原 raw 日志。
- Ledger/ownership：原 current strict 与原八 selftest 只读一次全部通过，无物理坏副本或 F-122 新行；两 actual canonical、原工具/baseline及全部15025 nonowned 均 before=HEAD=final SHA。完整15051 tracked NUL分区为26 owned+15025 nonowned，index空；Git changed+untracked 与 manifest 精确闭合；LF/CRLF/BOM保留，tracked diff及全部新增文件显式 whitespace 检查无 authored error，verbatim raw 原样保留。见 `ledger-readonly.json`、`nonowned-final.json`、`whitespace-final.json`、Git NUL证据。
- 身份绑定：E=`verification/evidence/task-27-inspection/`；源 freeze=`E/source-freeze-01.json`，SHA `578875152095290d0ec0cc8ebb024c246c7b7938975a93a349f944d1b222a138`。Task 计划/Result前 prefix 为 21467 bytes，SHA `5384f2522338538b3a3269f1b2a05dc1d963eebd6787a55bdf5d79b6412b6952`；Result 从此 prefix 后 append。完整 Task 最终 bytes/SHA由 producer交接实际返回，不能把旧prefix冒充完整Task。manifest=`E/manifest-final.json`，SHA `ba1aa854998cea6fb5f40dcffec3de64130df712429e5f08414cff5077bab70a`；manifest排自身hash、Task行仅绑定上述精确prefix以避循环，其余每项whole-file bytes/SHA。
- 完成范围和局限：F-122 本单元闭合证据在本 Result/E，不关闭任何其它来源/过渡/未迁协议 ledger 行。原两 exact Dimension 失败和十八 Skip 未修复；无实机/部署或单 CMD≤2s 声明，后者仍属 T-012；Cera业务/Legacy footprint/恢复catch、F-07及其它Spec002剩余义务不变。没有 stage/commit/push/PR/merge/remote/版本/tag/部署/游戏/cleanup/Task28。所有本 Task native 均自然完成，最终 writer 在本 Result/manifest 写完即停止，交主控 fresh required 快审。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

- Reviewer：fresh independent `b_task27_review`；与 planner、producer 和主控分离。应用 `reviewing-work` 的 Task Unit、Code、Deliverable 判据；本轮仅为 TASK-27 contract r1／Result R1 的局部结果快审，不批准 Spec002 整体完成，不代主控 checkpoint 或最终核验。
- 精确目标：review 前 Task 完整 25883 bytes，SHA `9b7cf888018681232cdc579cad16cd8de691a42684045b0c3521e0b994180da8`；其中计划 prefix 21467 bytes，SHA `5384f2522338538b3a3269f1b2a05dc1d963eebd6787a55bdf5d79b6412b6952`。HEAD `90b88aae3b07f0ec3dbb25eae22e2463ac90344f`；source freeze01 SHA `578875152095290d0ec0cc8ebb024c246c7b7938975a93a349f944d1b222a138`；manifest-final SHA `ba1aa854998cea6fb5f40dcffec3de64130df712429e5f08414cff5077bab70a`。本 review 只在此 exact prefix 后追加，manifest 对 Task 的 pre-Result-prefix 绑定不变。
- 已读 current Task／Result、Spec B3 RQ-013 与 AC-008/009/011、Plan P4 T-004/F-122、适用 AGENTS、实际关键源码和完整 owned diff／有界对照、原强测试与新 helper/Fact、正式原日志及证据。未发现本单元的未解决阻塞缺陷。

### 实际源码与范围

1. 独立将 26 份 before 源逐 bytes 对照 HEAD，再对完整文件进行限于本 Task 声明标识符、注释、两文档区间和唯一新增 Fact/helper 的精确归一；其它 bytes 不变。四枚举成员仍按首项、Declined、Gone、Failed 排列，无显式底层类型或新值／alias，故底层 int、0/1/2/3、default0 保持。live 源旧 `NotificationOutcome.Delivered`／旧 Party 登记属性与相关 alias 消费检索为空；可靠事件、transport 和其它 Delivered 未被泛化替换。
2. 逐分支核对 real `NotificationBus.AttemptAsync`：取消／精确 current→Compose→空 Declined→再取消→原 SendBatch 或缺 context Debug drop→返回 Registered，原取消重抛及 Warning/Failed 保留。GM 原 distinct accounts、缺 session offline、Failed 累计抛出和成功 attempted 未变。六个 DateTimeOffset 日切和 Mirror UTC+8 DateOnly 的键、prune、period、登记前后 current、精确 CharacterId/outcome 水位条件不变。Party 原 Distinct 顺序、两轮上限、只重试 Failed、成功集合和最终原 audience 顺序不变，Town 两处仍在 publication 后 read-repair。
3. 新 Fact 的入口确实从 NotificationWorld 原 provider 解析 GameplayExecutor 和真实 INotificationBus，在同 publishing job 内 PublishAsync 返回后断言 Registered、原 Sender.Registered 与 Sent 都为空；job 结束并按原 SettledAsync 排空后，核一批、精确 connection 和 one/two 两消息实际顺序。没有 fake result/bus、第二 provider、提前 flush 或生产 API 变化；原 Publish/Project 入口和 finally 保持。旧 helper 快照是已披露的最初 CS1501 版本，与最终 helper 的唯一差别是补 RunAsync 必需 `CancellationToken.None`；old02 原 metadata/log 证明修正后编译执行并通过。该修正不被冒称行为 RED；旧／新 Fact 除 enum 名相同。
4. AGENTS 仅当前结果定义，NOTI-0x0009 仅人工区当前 town publication bullet 改为接受登记／登记顺序／不承诺物理收到。完整 bytes 归一证明机器区、历史客户端、抓包／截图、证据等级和其它正文不变，符合当前实现；Cera 业务、F-07、三许可及其它未迁边界均未被夹带修改。

### 独立证据核验

- 重建原八 selected full 与本轮八 fresh full 的完整 display/status/multiplicity Counter：9013→9014，唯一 N 是新增观察 Fact，D0、rename0、statusChanges0。Application5237、Protocol1778、Networking199、Host203、HttpApi57、Infrastructure976、Persistence484、Pvf80；8994 pass＋原 exact2 Dimension fail＋原18 skip。逐项核实际无 filter／--no-build／原5m deadline，八项目自然退出；App exit1 仅对应原两条确切 Dimension case，其余 exit0；本轮 Host203 全过，无重试或新增豁免。Release --no-incremental 原 metadata/log 为 exit0、0 warning/0 error。
- 原 focused App64→65、Protocol106→106 全部旧 case 保留；旧名观察 old02 一例通过；GM6 与七 quota14 的确切参数身份均在正式 Protocol full pass。只改 enum 引用的旧断言和驱动在完整文件归一中原样保留。Characterization10、七 actual controls 均逐 case pass／无 skip，Golden15 实际 bytes 均与 HEAD 及 f01dc226 相等。
- 只读重新执行记录的原 violations.py CLI／assemblies／九项 expected class/count，exit0，74 records、7 controls、outside/exe/orphan0、新 intentional0；未改变 expected 或拼接多轮。current strict validator 和原八 selftest 只读再次执行，均通过且拒绝原因与已存证据精确相同；没有新造物理坏副本或改工具。
- 实际 current 与 before capture 的 runtime、payload、source-registrations、crosscheck 四文件逐 bytes 相同；pure family before/after 逐 bytes 相同，三类 registration exit 全部 owner 保持，非凭总数推断。相应原命令、console、退出码及 source/driver freeze 均由完整 manifest 校验。
- 完整 NUL tracked 集合为15051，26 owned／15025 nonowned 互斥且完整；独立检查所有 nonowned 当前 bytes/SHA 与 before 相等，Git tracked diff 恰为该26，index 空，两 actual canonical 为 nonowned 并保持不变。manifest 共180路径，包含一项明确 self-excluded 的 manifest 本身，其余179项 whole/prefix hash 均逐项复算；实际 changed∪untracked 与该180路径精确相等。当前26 source 及五 driver SHA 均匹配 freeze；LF/CRLF/BOM保持。独立检查当前全部67 authored/new text 路径无 trailing/blank-eof/space-before-tab 问题，87项 verbatim raw 保原bytes，tracked diff 的完整 whitespace 检查 exit0。Task26只读 EOF 空行未进入本次 diff。

### 批准范围与保留

本轮批准仅覆盖上述 F-122 命名闭合和真实登记时序观察，没有新可见行为或注册迁移；无阻塞 finding。原两条 Dimension 失败、18 Skip、exe0 观察边界保留；不主张实机、性能≤2秒、Cera迁移、F-07或Spec002整体完成。reviewer 未运行 native、未改产品/Result/其它证据、未stage/commit/remote；此追加完成后停止写入，交主控继续消费与本地检查点。

## Controller Consumption R1

主控已完整读取 current Task r1、Result R1、独立 Quick Review R1，并核实 reviewer 与 producer 分离且双方 writer/native 已停止。批准目标精确匹配：Result 完整前缀 25883B SHA `9b7cf888018681232cdc579cad16cd8de691a42684045b0c3521e0b994180da8`；含 review 的消费前 Task 32300B SHA `23554d9ee502bd7b28473e4634ea42c1e9bf399363c73940331e74a040c64f0e`。review 为 approved，无未解决阻塞；本段仅在该前缀之后追加，不修改 Result 或独立判断。

主控逐项读 actual owned diff、新 Fact/helper、真实 bus/GM/Party/Town/七 quota 分支与两处文档，确认仅许可名称和单个观察变化；四枚举值/default0、原 await/分支/周期/重试/投递顺序不变。另以独立 stdout-only 检查重建八 full 的逐 display/status/multiplicity Counter：9013→9014，仅 N1/D0/rename0/statusChanges0，8994 pass＋原 exact2 Dimension fail＋原18 skip；全部原用例保留，Host203本轮通过。source freeze01 仍为 `578875152095290d0ec0cc8ebb024c246c7b7938975a93a349f944d1b222a138`，Release0/0、八 fresh full、GM6/quota14/Char10/七 actual controls 与 Result 指向一致。

主控还独立逐项复算完整 manifest 的179个 whole/prefix hash；连同 self-excluded manifest 路径实际共180，与 Git NUL changed∪untracked 精确相等，不再另加 self。manifest SHA `ba1aa854998cea6fb5f40dcffec3de64130df712429e5f08414cff5077bab70a`。完整 tracked15051＝owned26＋nonowned15025，所有 nonowned bytes/SHA exact，含两 actual canonical；capture四文件、pure family before/after逐 bytes 相同，Golden15各自 current＝HEAD＝f01dc226。按实际记录重新执行原 violations.py CLI/expected/assemblies exit0，74/7、outside/exe/orphan0；原 strict/selftest由独立快审另行只读复核通过。完整 whitespace与行尾核查无 authored 问题；原 Task26只读 EOF告警不属于本次 diff。核验不运行 native、不重写原证据。

允许在当前任务分支将这180个精确路径作本地检查点；提交前后核父提交、完整NUL路径、index/工作区和footer。只有本地提交中 `Outcome: done` 成立后 TASK-27 才计 done。此单元只闭合 F-122，不关闭其余 ledger 行，不声明 Spec002 或性能/实机完成；接下来先对 Cera 未迁旧路径作有界事实取证/边界裁定，再经受影响 Plan 或必要 Spec 门继续，不能暗扩四 opening commit-result、typed保护或 raw许可。中文、不push/PR/merge/版本/tag/部署/cleanup及Astra后用户明确收尾限制继续有效。
