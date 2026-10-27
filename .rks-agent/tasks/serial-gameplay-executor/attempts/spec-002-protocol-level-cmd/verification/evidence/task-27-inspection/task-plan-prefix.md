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
