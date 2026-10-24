# TASK-05 — 完成 Channel Connect／Script／Info 协议 CMD 迁移

## Task Unit Contract

- Revision: 2（r1原计划保留；按文末裁定补齐两个真实线测试的探针重接线）
- Outcome and references: 交付父 [Plan P1](../plan.md) T-003 的剩余 Channel 小闭包，对应 [Spec B1](../spec.md) RQ-001/003/004/007/009/012/014/015、AC-001/002/004/005/007/009：Connect、Script、Info 都成为同一总线上的协议级 CMD，消息 handler 仅交付原请求和精确 context；两条旧查询命令及 result 删除。保留原当日 key、原领域读取、无事务形状、响应／连接／线字节和顺序。
- Owned boundary: 下文精确生产／测试文件、本 Task、B 的开放迁移账，以及 `verification/logs/task-05-*`／`verification/evidence/task-05-*` 证据。冻结 baseline、已提交 Task、父 Spec/Plan/契约和 A 封存制品只读；不迁 LOGIN／频道入场策略／名册，不启用效果状态或 MainSettlement，不改共享执行骨干。
- Required checks: 三薄 handler 原输入交付、三真实 CMD 响应／原 connection、独立 key 向量与有效坏变异；真实 TCP 四个 Channel 协议序列及既有 F129；完整 Release 构建、完整 Application／Networking／Protocol 回归、新鲜实际程序集 DFO／control，Golden 与数量账；当前注册 capture、五行闭合及严格校验、范围／行尾检查。见验证段。
- Dependencies: TASK-04（`5d83ce884b43c5dc027583d8f6472806fcb3d9c0`，同一总线、typed route、显式非效果请求、窄登记能力及已快审完整八项目结果）；当前数量账8420。
- Result review: required — 本单元改变三条消息接线、删除旧 API，并抽取同时服务 Connect 和加密响应编码器的 key 算法。错误可能表现为只在跨本地日期／非公历 culture 下握手或解密失败；需独立核对实际 key oracle、原断言迁移、注册闭合和线字节，不能只凭编译或新薄 handler 替身测试提交。

## Detailed Plan

### 1. 起点与精确文件边界

开工确认 HEAD、原 `task/refactor-session-20261021` 分支、干净工作区除本计划、Task 唯一身份和已提交依赖；沿父授权实施、验证及本地检查点，不推送／PR／合并／远端修改／部署／清理，不运行 df_game_r／DNF.exe。执行者读 AGENTS 及两份架构偏好文档；无 `.codegraph/`，直接按当前 rg 路线。主控组织 required 快审与提交。

允许最终生产改动均位于 `Server/src/Dfo.Server.Protocol/`：

- `Channel/Connect/ChannelConnectHandler.cs`；新增 `Channel/Connect/ChannelConnectCommand.cs`。
- `Channel/Script/ChannelScriptHandler.cs`；删除 `Channel/Script/GetChannelScriptCommand.cs`，新增 `Channel/Script/ChannelScriptCommand.cs`。
- `Channel/Info/ChannelInfoHandler.cs`；删除 `Channel/Info/GetChannelInfoCommand.cs`，新增 `Channel/Info/ChannelInfoCommand.cs`。
- `Channel/ChannelProtocolKeyProvider.cs`；新增共享纯算法 `Channel/ChannelProtocolKey.cs`。
- `ProtocolModule.cs` 仅本三协议的命令／消息接线。

允许测试改动：

| 相对 `Server/tests/` 的路径 | 责任 |
|---|---|
| `Dfo.Server.Application.Tests/Channel/ChannelConnectHandlerTests.cs` | 薄 handler 原请求／精确 context 交付；原日期 key／原 connection 断言迁到实际新 CMD 向量测试 |
| `Dfo.Server.Application.Tests/Channel/ChannelControlMessageHandlerTests.cs` | Script／Info 改验新协议 CMD 的原输入交付；保已有 ScriptVersion |
| `Dfo.Server.Application.Tests/Commands/Channel/ChannelControlCommandHandlerTests.cs` | 原 Script bytes／catalog 断言迁至真实新 CMD 的登记结果和原 connection；保已有 ScriptVersion |
| 新 `Dfo.Server.Application.Tests/Channel/ChannelProtocolKeyTests.cs` | 固定时钟、跨本地日界／culture 的独立字面量向量，同时检查 provider 与真实 Connect CMD |
| `Dfo.Server.Networking.Tests/Integration/ChannelControlMessagesIntegrationTests.cs` | 在原真实控制协议序列加入 Connect 帧，核对响应 header／36B 明文 body；原版本／Script／Info 加密字节及 F129 两例保持 |
| `Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 如需明确证明本四 Channel route／旧注册消失，只补对应注册断言，不改其它规则 |

`ItemIdentityArchitectureTests` 与 `CommandHandlerArchitectureTests` 对 ChannelConnectHandler 的引用仅取其 Assembly，不因本迁移修改。所有 Messages、decoder、encoder、传输、骨干、领域服务和其它测试文件只读。实际发现白名单外必需消费者或 shared contract 变化时先向主控给具体证据和最小增补。

AGENTS.md 只更新当前 opt-in 路由那一句，从仅 ScriptVersion 改为四条 Channel＋GM 启动，准确保留其它业务 legacy／效果未启用说明。B 允许开放 `verification/migration-ledger.json/.md`；两 probe 工具仅在当前 capture 确需适配本批 API 时作有界修正，不能重建平台或重写 `migration-baseline.json`。本 Task 与 task-05 前缀日志／证据归本单元。

### 2. 直接复用现有协议骨干，保原无事务语义

先读取 Task04 当前 `ChannelScriptVersionCommand`／handler 和实际注册扩展，使用同样的 `IProtocolCommand<TResult>`、`IProtocolCommandMessageHandler<TCommand>`、强类型 `ProtocolFrameMetadata.Channel(Request.Metadata)` 与具名完成值形状。每条新 CMD 带原 request／NetworkDispatchContext；消息 handler 只构造并等待该 CMD，不注 sender、provider、领域服务或登记能力。不要反射 Metadata，也不调用旧细命令或其它 CMD。

CMD 仅注入已批准能力：

- Connect：TimeProvider＋ICommandMessageOutbox，纯协议 key 算法仍归 Protocol。
- Script／Info：IChannelDirectoryDomainService＋ICommandMessageOutbox。

Script 直接 `ReadScriptAsync(ct)`，登记原 `ChannelScriptResponse`；Info 直接 `ChannelsAsync(ct)`，登记原 `ChannelInfoResponse`。接收者仍为原 Dispatch.Connection.ConnectionId，原响应 DTO／编码器不变。Connect 计算原 key 后登记原 `ChannelConnectResponse`。三者没有旧事务或选中会话参与者，不引入 Write／ReadOnly 单元，不用旧 pipeline 或 fake DomainService 包内容源。ScriptVersion 已完成，本轮保其业务不动。

删除 GetChannelScriptCommand／GetChannelScriptResult／handler 和 GetChannelInfoCommand／GetChannelInfoResult／handler，以及各自唯一 Direct 注册。当前全仓源码消费者已核实仅各消息 handler、ProtocolModule 和上述两 App 测试文件；执行时仍复核，不能用残留 alias／转发层保 API。接线用实际 `AddProtocolCommandHandler<TCommand,TResult,THandler>` 与 `RegisterProtocolMessageHandler<THandler,TMessage,TCommand>`，绑定本三原消息 enum／CLR。既有 14 遥测、未注册枚举／帧语义和其它 legacy 路由不动。

### 3. 当日 key 的唯一算法与独立 oracle

实际 `ChannelProtocolKeyProvider.GetCurrentKey` 返回 **字符串**：`timeProvider.GetLocalNow().ToString("yyyyMMdd'000006'", CultureInfo.InvariantCulture)`。它不是 hash／加密派生。原 key 14 ASCII 字符；Connect encoder 产出36B明文 body，前4B零、key复制到其后并补零；其它 Channel encoder 给 AES key 尾补零。抽取一个只接本地 DateTimeOffset、返回格式串的 Protocol 纯函数，由 provider 与新 Connect CMD 共同调用；时钟读取仍显式 `GetLocalNow`，不改 UTC、不用当前 culture、不复制第二份格式算法。provider仍服务原 ScriptVersion／Script／Info编码器，DI不变。

保留原 `20260810000006` 向量，并选至少两个可区分本地日界的明确输入，例如 UTC `2026-08-09T16:30:00Z`＋UTC+8→`20260810000006`，UTC `2026-08-10T01:00:00Z`＋UTC−8→`20260809000006`。预期为上述独立字面量，不以 helper 输出互证。每向量调用实际 provider 和实际 Connect CMD，断言相同预期、原 connection 和一条正确响应；时钟用现自定义 TimeProvider 形状，GetUtcNow返真实 UTC、LocalTimeZone独立指定。

至少一个向量在明确会采用非公历年份的 culture（如 th-TH）下执行，确认仍是公历字面量。**culture在 TestGameplay 的作业内设置并 finally 恢复**，不要假设调用者 ExecutionContext会流入执行器，不修改 DefaultThreadCurrentCulture。若环境不支持候选 culture，先选择当前运行时可用且可区分的明确文化／calendar并记录，而不取消该区分。

先应用 test-value gate：薄 handler／旧 DTO 删除是机械迁移，不造编译 RED；key 是共享可观察合同，采用一个有效坏变异证明向量会拒绝，例如只将新 Connect CMD 的 GetLocalNow 暂换 GetUtcNow，使跨日向量在响应 key 断言红，随后原字节恢复绿。编译／fixture未建立 culture或时间前置失败不计 RED。新增 helper 和生产 key 调用均在最终白名单，变异后恢复最终正确实现，保持所有 codec 文件无diff。

### 4. 测试语义迁移与真实线级顺序

原 Connect handler 用例的日期／同 connection 断言转移至上述真实 CMD 向量；原 handler 用例改为检查原请求引用、原 context 引用及只一次 SendAsync，像 Task04 的 ScriptVersion。Script／Info 的旧 Get result类型断言因 API删除不保留，但原 bytes／channels 值与原 connection 必须在新实际 CMD/outbox 用例中保留；其薄 handler 用例同样检查原 request/context，不让 stub bus 返回旧查询 DTO 后自行组包。已有 ScriptVersion 用例不删或放宽。

真实 `ControlMessages_TraverseTypedCommandsAndPreserveLegacyPayloads` 的固定 UTC+8时钟／脚本／频道声明保持，先发0x0B并读0x0C，要求原 classification 0x7C、checksum0、ack1（可仅给本文件ReadFrame helper增观测字段）和明确36B body：前4零＋ASCII `20260810000006`＋余零。随后原0x05／0x09／0x11序列按原顺序执行，版本 ciphertext 的独立十六进制常量、解密脚本内容与padding、频道各字段断言原样保留。新CMD在同一容器和作业登记，不能以不同container的 outbox假造真实入站。原 F129 Debug／入队拒绝Error两例保持，不再次临时改 NetworkPlugins，也不修改日志级别或期限。

本三协议无事务、无selected参与者及scoped disposable组合，Task04已披露的scope双故障残余在本批不适用；不借此扩修骨干。新route继承Task04唯一入队、Closed/leak／早期outbox保护，完整App回归继续守原反例，不复制一套执行机制测试。

### 5. 五行迁移闭合与数量

只闭合基线三消息 `message:11`（Connect）、`message:9`（Script）、`message:17`（Info），以及旧 `Dfo.Server.Protocol.Channel.Script.GetChannelScriptCommand`／`Dfo.Server.Protocol.Channel.Info.GetChannelInfoCommand` 两行。各行给原无事务→新无事务、无selected参与者的有证据 none_reason、真实新CMD／保护位置／测试与本Task完成证据。Connect原无细CMD，新CMD由该消息行覆盖；不得往冻结baseline加新旧ID。Task04原四closed行与其original字段保持，不动其它pending或非协议来源。

当前 capture 的合理预期是消息仍173，命令205／实际handler204，route 4Protocol＋1Internal＋200Legacy；Kind169Tx＋23Direct＋9RO＋4Workflow。命令只增加1是Connect新增、Script／Info各换1条；这些数字是交叉检查，不是硬填结果。导出实际CLR/source应逐条吻合。完成后业务消息155、旧命令200、来源72仍pending，不能据四Channel完成说T003／Spec完成。

沿用现工具精确当前Task闭合预检：产品检查与映射先过，写本Task Result后跑正式strict validate／selftest并更新同一R1 Checks；不伪造done、不借Task04冒领。保存当前capture、五行workunit-map、preclose及strict／8坏数据原报告。不得修改历史baseline、旧Task或A证据。

### 6. 本轮适当验证与交回

所有正式dotnet使用本机 `require_escalated`／caffeinate，原xUnit类并行、原deadline／Skip；先完整 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 要求0 warning／0 error。随后完整以下三项目，均不加filter：

```text
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

Task04刚完成完整八项目，本批只改Protocol三切片和共享纯key／相应测试，不重跑无实际影响的Host／HttpApi／Infrastructure／Persistence／Pvf；完整构建仍覆盖它们，T012最终八项目义务保持。若执行中不得不改共享骨干／部署或其它程序集，先由主控重核边界与检查范围。

本轮新鲜同一DFO目录只含实际App／Net／Protocol三个程序集，各唯一control。沿原 violations.py：App62（原61＋Task04的ProtocolCommandExecutionTests反例1）、Net7、Protocol control1，合70；App原expected为CommandBusExecutorTests1、EffectOperationReplayTests1、GameplayOutboxTests3、GameplayQueueLeakTests1、GameplayWriteAssertionTests53、NotificationDeliveryTests1、ProtocolCommandExecutionTests1，Net为OutboundRealSocketTests1及OutboxRegistrationTests5。其它五程序集未执行，不要求其control；反例/control外0、exe0、orphan0，原类数不放宽。新例均在executor中，不应新增违规，遇新记录先诊断而不调expected求绿。

起点App4680（4662通过＋2准确原Dimension内容失败＋16skip）、Net199全过、Protocol1755全过；新增N／删除D按实际Fact／Theory case分项，整体8420+N−D，不能把旧驱动更换算删测试或凭类名计算。仅两条既有DimensionDungeon方法及Dungeon7100/maze0/cell(1,0)/entrance Left错误可作基线，其它失败先按AGENTS身份规则处理。其它项目没有本Task重跑声明。完整Net含原表征目录10例，应逐名确认10通过0skip；不另重复定向运行已完成表征。15 Golden与f01dc226及Task04零diff，原codec／线序／连接开关断言保持。

每轮用唯一task-05日志／证据路径，保cwd／HEAD／实际命令／UTC／退出／耗时和实得资源；记录行尾/BOM前后，CRLF／混合文件用字节定点编辑，不format；检查 `git -c core.whitespace=cr-at-eol diff --check`及新文件。精确搜索GetScript／GetInfo旧API引用0；无新sender/bus注入CMD、无WorkUnit或新ambient，未改骨干／原受保护文件。最终源与证据SHA、原断言保留／驱动变化、N-D、实际capture／五行闭合及剩余范围写入Result，required独立快审后交主控检查点；不自批或提交。


## Controller Disposition — r2 探针重接线消费者补齐

- Decision: `task-rework`；保持本Task未提交，Spec/P1及生产接线规则不变。
- Evidence: 完整Networking首轮19项失败同根：`Integration/LifecycleWireHost.cs:88–92`与`Integration/InboundExecutorWireTests.cs:473–476`将ChannelConnect消息注册改为legacy ProbeHandler，但留下本轮新增的ChannelConnectCommand Protocol注册。`MessageHandlerRegistry`的反向闭合正确拒绝失配；未启动后的Stop异常为次生问题。主控已直接读取两个重接线函数确认，不放宽registry/旧测试断言。
- Amendment: 精确新增最终可写测试fixture `Server/tests/Dfo.Server.Networking.Tests/Integration/LifecycleWireHost.cs`、`Server/tests/Dfo.Server.Networking.Tests/Integration/InboundExecutorWireTests.cs`，仅在替换消息为legacy probe时同步移除该被替换消息原绑定的Protocol CommandHandlerRegistration。当前Connect为单消息绑定；实现须按真实被替换绑定处理，勿误删其它仍使用的protocol注册。可加必需using/局部括号，无业务/探针/原断言/标签/期限/teardown吞错改动。
- Checks / readiness: 第一轮App与Protocol通过并已保存原证据。此修正只测试fixture，生产/App/Protocol源未变；完整Release重建及完整Networking新鲜独立DFO复跑足够，原三程序集第一轮取证与最终Networking独立取证按实际control/expected分别核查，不混合jsonl；最终Networking表征10逐名通过、原断言和F129保持。required快审包含两fixture，N/D不因驱动修正增加。无新Task依赖/用户决定，允许恢复实施。


## Result

- Outcome: done（实施轮 R1，Task 契约 r2；待 required 独立快审和主控本地检查点，不表示 T003 或 Spec 完成）。
- Changed: Connect／Script／Info 三薄 handler 只交付原 request、精确 NetworkDispatchContext 和一次同 bus SendAsync；三新协议 CMD 直接计算原 key／读取原 domain bytes 或 catalog，并经 ICommandMessageOutbox 登记原 response 到原 connection。三者不打开 Write／ReadOnly 事务，无 selected 参与者；旧 GetScript／GetInfo 命令、result、handler 及两个 Direct 注册删除，源码引用为零，无 alias／转发。provider 和 Connect 共用一个 Protocol 纯 key 函数，显式 GetLocalNow、InvariantCulture、公历14字符 yyyyMMdd'000006' 保持；所有消息／codec／encoder 与骨干不改，效果／MainSettlement 仍禁用。
- Checks — oracle／断言: 三独立字面量向量保原 20260810000006，覆盖 UTC+8、UTC−8 本地跨日，且在 TestGameplay 作业内设置并 finally 恢复 Thai culture；provider 与真实 Connect CMD 各对字面量断言，不用 helper 互证。正确实现 green 3/3；仅 Connect 临时 GetLocalNow→GetUtcNow 时1通过、2个跨日响应 key 断言失败，exit1；finally 原字节恢复后 green 3/3、exit0，非编译／fixture RED。见 [mutation](../verification/evidence/task-05-preflight/key-mutation.json) 和 key-red-01／key-green-01、02 原日志。薄 handler／旧 DTO 删除为机械驱动迁移，不造行为 RED。Connect 原日期／原连接断言迁入真实 CMD 向量；Script／Info 原 bytes／catalog／原连接转入真实 CMD/outbox，旧 DTO 类型断言随删除退出，原用例数量及 ScriptVersion 不变。
- Checks — 线级: 原真实控制序列先增 0B→0C，明确 classification7C、checksum0、ack1、36B body＝前4零＋ASCII 20260810000006＋余零；随后原05版本独立 ciphertext、09脚本内容及 padding、11各频道字段和顺序保持。F129 Debug／入队拒绝 Error 两个原断言不动。最终完整 Networking 含三个控制序列用例及原 Characterization10例逐名通过、0skip；15 Golden 对 f01dc226 和 Task04 无 diff，没有额外重复表征轮。
- Checks — 正式: 最终 task-05-formal-build-02 完整 Release --no-incremental，17.683s、exit0、0 warning／0 error。完整 App 首轮4683＝4665pass＋2原 Dimension fail＋16原skip，114.214s、exit1；Protocol 首轮1756/1756，4.528s、exit0；最终 Networking 第二轮199/199，53.930s、exit0。原 xUnit 类并行、Skip、5m deadline、无 filter、native require_escalated／caffeinate 保持。App仅 Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource 与 All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot 失败，均原 Dungeon7100／maze0／cell(1,0)／entrance Left InvalidDataException；没有其它最终新增失败。本批选择的三项目总6638，未重跑其它五测试项目；完整 build 覆盖其构建，T012最终八项目仍待完成。[正式汇总](../verification/evidence/task-05-inspection/formal-summary.json) 和各同名 log/json 保存实际 cwd／HEAD／command／UTC／exit／耗时／Darwin children_maxrss；资源只作观测。
- Checks — 首轮真实失败／r2: 第一次完整 Net 为199＝180pass＋19fail、exit1，原日志保留。两 legacy probe fixture 替换 ChannelConnect 消息注册却残留其 Protocol command 注册，MessageHandlerRegistry 反向闭合正确拒绝；Host未启动后的 StopAsync 异常为次生错误。经主控 r2 精确增补，仅在替换原单消息绑定时移除对应 Protocol CommandHandlerRegistration，仍用的绑定、生产 validator、probe/assert/标签/期限/teardown 均不改。修后重建全解决方案并在独立 fresh02 目录跑完整 Net；生产／App／Protocol 源未再变，按 r2 复用首轮 App／Protocol，不冒称首轮全过。
- Checks — DFO／数量: 首轮三实际程序集 fresh01 独立70 records（App62／Net7／Protocol1），各唯一 control，原 expected 各类数量全部匹配，外0／exe0／orphan0。最终 Net fresh02 另有7 records、唯一 Net control、OutboundRealSocketTests1／OutboxRegistrationTests5，外0／exe0／orphan0；violations 两轮各 exit0，没有复制或混合 JSONL。N4／D0／merged0＝App key向量3＋Protocol四Channel注册1；旧薄handler／CMD驱动替换、原TCP加步骤及两fixture调整均0增删。全库数量账8420＋4＝8424，仅为闭合数量账，不冒称本轮八项目重跑。[数量与断言迁移](../verification/evidence/task-05-inspection/test-delta.json)。
- Checks — 开放账: 当前 capture 仅 ConfigureServices／枚举注册，无 BuildServiceProvider 或业务执行；实际173消息／141handler、205命令／204handler共378条源码↔runtime双向闭合，Kind169Tx／23Direct／9ReadOnly／4Workflow；command route4Protocol＋1Internal＋200Legacy，消息4Protocol＋169Legacy。[capture](../verification/evidence/task-05-capture-01/crosscheck.json)／[routes](../verification/evidence/task-05-inspection/current-routes.json)。只闭合 message:11、message:9、message:17 和旧 GetScript／GetInfo 两行，原新无事务／无参与者及保护证据见 [workunit-map](../verification/evidence/task-05-mapping/workunit-map.md)，Task04四closed行、baseline与所有original不改。preclose有效且只列本Task待Result；产品／预检过后落盘本Result，再以原版strict validate与selftest分别运行，均exit0，completion_results_pending为空、8个坏副本全部拒绝。见task-05-ledger-strict-01／task-05-ledger-bad-data-01及mapping的strict-final／bad-data-final.json；本句是同一R1 Checks据真实结果更新。
- Checks — 范围／SHA: [source-checks](../verification/evidence/task-05-inspection/source-checks.json) 记录22个授权产品／测试／AGENTS／账本路径的最终SHA、2删除null、原行尾／BOM；2545个原tracked其它源／旧Task／baseline均与开工逐文件SHA相同。core.whitespace=cr-at-eol diff --check及新文件空白检查通过，旧API源码引用0；所有Messages／decoder／encoder／共享执行骨干无diff。最终源及证据文件SHA见 [artifact-manifest](../verification/evidence/task-05-inspection/artifact-manifest.json)，manifest排除自身及可追加审查的Task，另记录执行正文R1快照SHA。运行／编辑脚本的真实历史及原断言迁移说明在 [执行证据](../verification/evidence/task-05-inspection/README.md)。
- Remaining: 155业务消息、200旧命令、72执行器来源 pending；14遥测／12 outside 保原排除，五保护新归属及八过渡删除证据仍pending。四Channel完成不等于其余T003／LOGIN／entry／roster／效果生命周期完成；本批无事务／selected参与者／scope资源组合，不涉及Task04已披露的scope双故障残余。T011清零、T012最终八项目／同工具测量、单条2秒验收、Astra审核与用户要求分支收尾均未冒称完成。没有git提交／push／PR／合并／远端修改／部署／清理，也未运行df_game_r／DNF.exe；required快审及本地检查点交主控。


## Independent Result Quick Review — R1

```yaml
kind: rks-review
verdict: approved
```

Reviewer: fresh independent `b_task05_review`；目标r2/ResultR1、base5d83ce88，采用 `reviewing-work` Task Unit/Code/Deliverable。只读并复跑validate/selftest，没有重复native测试。96项SHA及Task R1快照匹配；三CMD/薄handler/注册符合范围、无新事务、旧查询API清零。唯一purekey算法及独立字面量/跨日/Thai非公历前置有效，UTC变异两个响应断言红且恢复绿。原数据/连接/线级断言保留；两fixture仅同步移除被探针替换的单消息CMD绑定，生产闭合和原探针断言不放宽。

最终build0/0；App4683仅准确原两失败/16skip、Net199/199、Protocol1756/1756。两DFO轮各实际control/原expected匹配、外exeorphan0、表征10通过。账仅五行变化，已有closed/original不变，strict通过/八坏data拒，N4D0与8424数量账成立。未发现阻止提交的局部缺陷。

主控消费：仅释放本Task本地检查点。R1 manifest原样保留，Task新增本审查段符合其前缀快照约定；继续T003的Login/真实频道入场与后续名册/效果等，Spec2与分支收尾未完成。
