# TASK-02 — 四项当前性保护的区分回归与延迟组装禁用判据

## Task Unit Contract

- Revision: 1（标识本契约与详细计划）
- Outcome and references: 完成父 [Plan P1](../plan.md) T-002 的先行保护部分，对应 [Spec B1](../spec.md) RQ-006～008、RQ-013～015、AC-003/008/009：F-88/F-93 的 `RecoverStamina.IsInWorld`、`Teleport.Owns(previous)`、Safety 调用 `DiscardExact`、Ledger Capture 后 `DemandCurrent(recipient)` 各有移除即红的真实区分用例；F-132 延迟组装保持生产禁用，并有会拒绝坏调用的全生产程序集方法身份判据。本单元不迁移协议、不修改产品行为，不关闭尚未建立的迁移账。
- Owned boundary: 下文精确文件清单中的测试、本 Task，以及本 B 子目录的 `verification/logs/task-02-*`、`verification/evidence/task-02-*` 证据。四个保护的原产品文件只允许临时单点变异并逐字恢复；最终产品 diff 必须为零。不编辑已封存 A、父 Spec/Plan/契约或已提交 TASK-01。
- Required checks: 四个独立去保护变异的断言 RED、原保护恢复后的 GREEN；禁用判据真实生产扫描通过且坏例确被拒；完整 Release 构建、完整 Application 与 Host 回归、新鲜各程序集 control/原 expected 的 DFO 取证；原断言/并行/期限/Skip、所有权、行尾、最终产品零差异及8358起的数量账。
- Dependencies: TASK-01（已提交 `e0604195b2ade73322984aec0b792604513d6270`，自有池 helper、Failed 日志分类与8358数量账）
- Result review: required — 后续 CMD 迁移将依赖这些守护；若用例被下层门掩盖、判据漏扫程序集或没有实际拒绝能力，错误保护可随迁移消失且普通全量不发现。须独立核对真实触发点、变异失败原因及扫描范围后提交。

## Detailed Plan

### 1. 起点与精确边界

输入为已批准 Plan P1 与 Spec B1、TaskContract r1；本地 HEAD 为上述 TASK-01 检查点。开始核对工作区/分支/HEAD、Task 唯一身份及依赖结果。沿父授权实施和验证，主控负责 required 快审及本地提交；不推送/PR/合并/部署/远端修改/清理，不运行 df_game_r/DNF.exe。当前无 `.codegraph/`，直接按拥有源码定位。

允许的最终测试改动：

| 路径（相对仓库） | 允许责任 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Inventory/WeaknessGoldRecoveryTests.cs` | 复用 ShopFixture/Weaken/Recover，增加 handler 前置在场守护；已有测试断言不改 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/TeleportConsumableTests.cs` | 复用 Fixture、真实 town/session 与当前视图，增加 previous lease 错代守护；必要 recording bus 限本文件 |
| `Server/tests/Dfo.Server.Application.Tests/Creatures/CreatureRuntimeDomainServiceTests.cs` | 复用真实 SQLite/runtime Fixture，增加真实 SelectedSessionSafety.BlockAsync 调用点守护及精确代数对照 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs` | 增真实 Ledger 在 Capture 与登记间失效的区分测试及本地 scripted query；不改变既有重放断言 |
| `Server/tests/Dfo.Server.Host.Tests/DeferredOutboxCompositionArchitectureTests.cs`（新增） | 全生产闭包 IL 禁用判据、坏调用/同名非目标的区分例；内部小型 IL reader 仅服务此方法身份判断 |

可临时变异的产品文件仅：`Server/src/Dfo.Server.Protocol/Game/RecoverStamina/RecoverStaminaSlice.cs`、`Server/src/Dfo.Server.Protocol/Game/Teleport/TeleportSlice.cs`、`Server/src/Dfo.Server.Application/Commands/SelectedSessionSafety.cs`、`Server/src/Dfo.Server.Application/Effects/EffectOperationLedger.cs`。预存 HEAD 字节/SHA256，每次变异只移除下面具名保护，运行对应定向例，finally 原字节恢复；四文件最终 SHA 与 HEAD 一致。F-132 产品机制不改。若候选确实不能区分，允许在本测试白名单内调整触发输入/观察边界，不能改目标行为或用源码字符串断言冒充四项行为保护；需越界或共享决策时交主控具体证据。

### 2. F-88：在真实 handler 边界守住两个提前返回

先读当前源与复用夹具，不通过真实 dispatcher 额外门间接证明 handler 门；用已有 `TestGameplay.RunAsync` 在执行器内执行真实 handler。

**RecoverStamina**：当前 `RecoverStaminaSlice.cs:140–142` 在 FindByConnection 成功后单独拒绝 `!sessions.IsInWorld(session)`；旧 `WeaknessGoldRecoveryTests:336` 直发细 CMD，不保护此提前返回。新增测试使用 ShopFixture/Weaken，记录金币/虚弱和 sender 初态；在 executor 内 Fence 精确 session，明确断言 FindByConnection 仍返回同 session、IsInWorld=false，调用已有 Recover（真实 RecoverStaminaHandler）。必须无回包、金币/虚弱不变。仅删 handler 的 IsInWorld return，预期会经过 CMD 拒绝并回复 error4，故无消息断言 RED；不能把现有命令内 IsInWorld 仍拒绝当保护已成立。原正常恢复用例为正对照。

**Teleport**：当前 `TeleportSlice.cs:93–95` 同时要求当前视图、previous member 与 `session.Owns(previous)`。用该测试的真实 Fixture、受支持请求及已 seed 道具；通过真实 ITownAreaDomainService.Enter 为同角色安装另一个 generation 的 lease，保持所选 session 与视图有效。测试先断言 previous 非空、当前视图非空、Owns=false；直接真实 TeleportHandler，不让 effect dispatcher 的其他门掩盖。验证没有 CMD dispatch、消息、道具扣除、位置/冷却变动。

若真实下层领域门仍使所有外部状态不变，使用**仅记录调用的本地 ICommandBus**在 handler 注入边界观察不得入 CMD，返回正常受控拒绝值即可；不抛假故障，不改变真实 session/town/currentView，也不靠假 bus 造错 lease。可在构造真实 handler 时替换该唯一边界而保持其余生产服务；无需编辑共享 ShopFixture。仅删除 Owns(previous) 子条件后，dispatch 计数必须从0变1使精确断言 RED。现有有效 lease 成功提交用例保留为对照。

### 3. F-93：Safety 的丢弃动作与 Ledger 的二次收件人校验

**DiscardExact 调用点**：当前 `SelectedSessionSafety.BlockAsync:64` 才是目标，现 `CreatureRuntimeDomainServiceTests:74–100` 只直接测试 runtime 方法。复用本文件真实 SQLite/runtime Fixture，窄 ServiceCollection 注册其 ISelectedSessionDomainService、ICreatureRuntimeDomainService 与 logger 构造**真实 Safety**，不需要完整 Host/关闭器。仿照现测试进入、推进/排 pending projection 与 room experience，再改变持久余额，使陈旧缓存若 Leave 会写坏余额。调用 Safety.BlockAsync(new SelectedSessionRecipient(...), 实际说明性的 uncertainty error, ...) 后，断言 runtime pending/due/projection 被清、随后 Leave 不把缓存落回、持久余额保持新值。

增加或在同一测试续段验证替换 generation 后，对旧 recipient Block 不清新 runtime：新 session 的 pending/due 保留。测试 setup 的所有写在 executor 内；现有 fixture 的测试会话服务可复用，别改其既有行为来制造通过。单删 Safety 的 DiscardExact 调用后，必须在 runtime/persist/pending 断言 RED；单纯断言 Fence 或 closer 不足。保留原直接 DiscardExact 测试。

**Ledger 的 Capture 后校验**：当前 `EffectOperationLedger.DispatchAsync:34–42` Capture 后检查物理引用，取得 ledger，再 DemandCurrent(recipient)，之后才写 LatestOrdinal。测试直接调用真实 Ledger，不能经后续 CMD pipeline 再拒绝。沿现 ShopFixture/EffectOperationContext/SelectedSessionSafety、真实 request metadata 和选角身份，用本文件局部 IOnlineUserQueryDomainService 装饰器（或同样窄的 scripted session query）让 Capture 首次校验有效、其后精确 recipient 变旧。首轮应在 handler sentinel 之前抛，未调用 sentinel；恢复查询有效后以**同一 ordinal/请求**再次 Dispatch 应首次接受且调用 sentinel，证明首次拒绝没有推进序号。

脚本化边界只控制查询返回的精确时间点，真实 Safety/Ledger/operation 保持；不要伪造 Ledger 自己抛错或读私有 LatestOrdinal。明确记录 Capture 与后校验各调用次数/物理对象，避免变异实际在 Capture 首次就红。仅删第二个 DemandCurrent(recipient) 调用后，sentinel执行或同ordinal被耗尽的明确行为断言应 RED。测试不把未提交 Pending 模拟为 Committed，不改现重放规则。

### 4. F-132：按方法身份禁止生产引用，不声称修复风险机制

实际 API 为 public `GameplayOutbox.ComposeAsync<TRecipient>`（`GameplayOutbox.cs:164`）；unit 内注册后提交时组装，当前生产无调用。其他同名通知 composer、IOutboxComposition 等不是禁用目标。Plan P1 的选择是不用此延迟 API；`EventService` 在未提交写事务拒绝即时投影的现保护也不动。

新 Host.Tests 判据直接复用已 linked 的 `GameplayJobHostClosure.ProductionAssemblies(Assembly.Load("Dfo.Server.Host"))`；现 Host closure 覆盖 Application、Protocol、Networking、Infrastructure、Persistence、Abstractions、Data、HttpApi、GmWeb、GameServer、Host及Dfo.Pvf，无需改 csproj。打印实际程序集名并断言 Host/HttpApi/GmWeb/GameServer/Application/Protocol 在内，避免仅扫描 App 测试可加载范围。Utilities 现无源码且不在闭包，记录该边界；若现场已不为空或出现新增未接线生产程序集，先报主控补覆盖，不静默遗漏。

枚举每个生产类型的 DeclaredOnly public/nonpublic static/instance 方法与构造器/类型初始化器，包括编译生成 async MoveNext/闭包；读取 IL 正确推进 opcode operand（含 switch），按 `InlineMethod`/`InlineTok` 可解析 method operand 处理 call/callvirt/ldftn/ldvirtftn 等引用，传 declaring-type/method 泛型上下文 ResolveMethod，比较解析后 MethodInfo 的 generic definition 与真实目标方法（DeclaringType/Module/MetadataToken等），不可只比字符串 `ComposeAsync`。现 `GameplayWriteEntryTableTests:802–855` 是本地解码参考；不要为一个判据重构共享测试平台或复制其整个表。遇到应解析 method token 的实际失败须带程序集/方法/token报告失败，不静默吞掉潜在调用。

生产断言必须无禁用引用。测试内保留一个未执行的坏调用方法（可async让引用落MoveNext），将它送入**同一检测器**并明确断言发现目标；再有同名非目标方法/调用证明不会误报。坏方法只用于 IL 分析，绝不运行或放进生产闭包。目标是可执行禁止使用的判据，不检查源码文字数量或更改 Commit/Rollback。记录 F-132 仍未修复，当前仅生产禁用；T-003 新登记接口不暴露该API及T-011最终复核仍是后续义务。

### 5. 执行顺序、验证与证据

先存原字节/范围与数量基数，写最小测试；当前保护下 GREEN 可作为设置校验。随后四项各自只移除目标保护，重编相应项目、只跑对应新增例取得有意义断言 RED，立即恢复原字节并重编 GREEN，再进行下一项。可先写一项/验证一项，不同时移除多个保护；编译失败、未建立前置输入、超时不算行为 RED。每轮唯一日志及证据目录，保留失败原文与恢复检查，不用修改期限/Skip/并行求绿。F-132通过生产0+坏方法命中+同名非目标不命中的判据区分；无需执行有风险的坏API。

正式全部 dotnet 在本机 `require_escalated`、caffeinate、原 xUnit 类并行环境运行，保留 `--blame-hang-timeout 5m`；允许 `-m:1` 串项目。完整 Release `dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 要求0 warning/0 error。最终完整：

```text
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

本单元新测试均在 App/Host；无最终 Protocol 产品或测试变更，因此不为临时已恢复变异额外规定完整Protocol。构建覆盖所有项目；其余六测试项目及最终八全量/性能仍归T-012。若最终改变超出上述实际边界，主控重新核对检查范围。

使用同一新鲜 `DFO_GAMEPLAY_ASSERTIONS` 目录，仅这两个正式项目，原 violations.py 核对实际程序集及原 expected：App `CommandBusExecutorTests=1`、`EffectOperationReplayTests=1`、`GameplayOutboxTests=3`、`GameplayQueueLeakTests=1`、`GameplayWriteAssertionTests=53`、`NotificationDeliveryTests=1`，App61条；Host只有其control1条；预期共62。新守护均在executor内，不应新增违规记录。保留 outside0/exe0/无测试帧0、两程序集各唯一control，未知记录先诊断，不放宽expected。定向/变异轮不强求未执行程序集control。

App起点4627=4609通过+2既有Dimension内容失败+16skip；Host197全通过。新增N/删除D依实际case/theory数量逐项记录，整体8358+N-D（不是本Task已运行八项目声明）；保持原两条 Dimension 方法及 dungeon7100 entrance Left 错误身份，不将其它失败冒充基线。原断言只新增不删/放宽，原测试驱动若为探针确需改动另列。保留正式命令、cwd/HEAD、环境、起止/退出码/耗时及实际取得的资源数字，日志原样存于B `task-02-*`，不改A。

最终 `git -c core.whitespace=cr-at-eol diff --check`、新文件检查及原产品4文件逐字/SHA复原；混合/CRLF文件字节级修改不format整文件。TASK-01清池helper及其语义保留；新测试不引入进程全清。结果应列四个去保护RED的失败断言与恢复GREEN、IL覆盖/坏例、N-D及边界，供 required 独立快审。迁移账仍为 T-002 后续单元，不能据本Task宣布 T-002全部闭合或Spec完成。


## Execution refinement — 真实输入与证据补齐

主控 `continue`：初始候选 Fence 不改变 `OnlineStateRegistry.InWorld`，guards-setup-02 在前置 Assert.False 失败，不计行为 RED。其实际判断是 phase/owner；测试改用真实 ReleaseExact→Select 同 session、不给 EnterWorld，保持 FindByConnection 命中而 phase 为 Selected，守护目标及产品不变。此输入调整在 r1 允许的白名单细化内，不改 Spec/Plan 或 Task 合同。

Safety 初版已杀掉目标变异，但自检发现同一时刻初次 Advance 不足以证明有实际变化对应的 pending projection。主控同意仅加强新例：推进60秒后再次 Advance 并断言变化非空，随后 Block 清空投影；重做 Safety 单点 RED/GREEN、最终完整构建及 App 全量复跑。Host 源码/产品未变，首轮完整 Host 证据保留，不复制混合 DFO。

## Result

- Outcome: done（实施轮 R1，Task 合同 r1；待 required 独立结果快审与主控本地检查点，不代表 T-002全部闭合或 Spec B 完成）。
- Changed: 精确5测试路径：WeaknessGoldRecoveryTests 增 selected 未在场静默保护；TeleportConsumableTests 增真实错代 town lease 不得入 CMD/不改资产/位置/冷却/消息；CreatureRuntimeDomainServiceTests 增真实 Safety.BlockAsync 丢弃旧runtime、pending reward/projection并保留替换代；EffectOperationReplayTests 增 Capture 后物理 recipient 变化，必须在 sentinel 与 ordinal 登记前拒绝且同ordinal恢复后首次接受；新增 Host DeferredOutboxCompositionArchitectureTests 按IL方法身份禁止生产引用延迟 Compose API，含 async/ctor 坏例和同名正常例。四处生产原保护均原字节恢复；没有最终产品行为改动。
- Why: 既有正常/旧CMD/直接Discard/物理context独立账本用例不足以守这些具体调用点，单删旧保护曾可逃逸。新例直接观察原静默、无权租约不得启动结算、精确runtime丢弃、recipient/序号语义；不经其它pipeline门掩盖。F-132是持续架构策略，采用真实method definition身份而非同名字符串，机制仍未修复，仅禁止生产使用。
- Checks — 变异: 原产品SHA及四个单点移除、RED/GREEN轮见 [mutations.json](../verification/evidence/task-02-preflight/mutations.json)。`task-02-red-recover-01` exit1：删 handler IsInWorld 后 error4回包使Empty红；`red-teleport-01` exit1：删 Owns 后 CMD调用从0到1；`red-discard-01` exit1：删 Safety 调用后 DueSession仍在；`red-recipient-01` exit1：删二次DemandCurrent后不再抛且可进sentinel。四个对应green均exit0/1例通过。Safety加强后的最终 `red-discard-02` 仍在Due残留Empty断言红，`green-discard-02` exit0，且变化非空前置先通过。四产品全部SHA与HEAD相同、Server/src最终diff0。编译修正setup01、无效Fence前置setup02均保留原日志，不冒充行为RED；setup03四例4/4通过。
- Checks — F132: `task-02-deferred-setup-01` exit0，3/3。扫描实际12生产程序集：Dfo.Pvf、Abstractions、Application、Data、GameServer、GmWeb、Host、HttpApi、Infrastructure、Networking、Persistence、Protocol；逐类型DeclaredOnly方法/构造器/类型初始化器含MoveNext/闭包，按IL operand解码、带泛型上下文解析InlineMethod/InlineTok，比较Module/MetadataToken对应真实泛型definition。生产引用0；同检测器确命中测试的async MoveNext和ctor坏引用，同名非目标不报。Utilities当前仅csproj、无源码/引用，边界如实记录。未执行坏API，不把禁用声称修复。
- Checks — 正式: 本机 require_escalated、caffeinate、原类并行/断言/期限/Skip。完整Release `formal-build-01` 18.269s、最终 `formal-build-02`21.895s，均exit0/0 warning/0 error。首轮完整 App `formal-app-01`152.883s，4631=4613通过+2原Dimension内容失败+16skip，exit1；完整 Host `formal-host-01`166.228s，200/200 exit0。只加强新Safety例后重新构建并最终完整 App `formal-app-02`119.283s，仍4631=4613+2+16、exit1。两轮仅 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，准确同一 dungeon7100 maze0 cell(1,0) entrance Left InvalidDataException；没有其它失败。Host源码/产品未变，不为新App测试重复Host。其它六测试项目仅完整构建，未宣称本Task已跑八项目或性能。
- Checks — 取证: 新鲜 `task-02-formal-01` App61+Host1=62条，两程序集各唯一control；独立最终 `task-02-formal-02` 只有App61条/一control。原violations.py按每轮实际程序集与原expected执行，`task-02-assertions-formal-01/02`均exit0，outside0/exe0/无测试帧0，未复制/混合jsonl。新增守护未产生违规；exe0保留仅观测的限制。
- Checks — 范围与数量: [最终5测试SHA](../verification/evidence/task-02-inspection/owned-tests-sha256.json)、[源/行尾自检](../verification/evidence/task-02-inspection/source-checks.json)、[数量账](../verification/evidence/task-02-inspection/test-accounting.json)。仅新增7 case=App4+Host3，删除0/合并0，整体数量账8358+7=8365，不是八项目实跑声明。原4测试仅增量新增，没有删除/放宽旧断言或修改旧fixture行为；Task01自有池helper保持，不引入全局清池，行尾保持。`git -c core.whitespace=cr-at-eol diff --check`及新源码尾空白检查通过。原日志原样保留，命令/环境/UTC/退出/耗时/rusage见各.log/.json，Darwin children_maxrss以bytes记录，只是子进程观测；总时长/内存不设阈值。
- Remaining: T-002的完整注册/来源/实际事务迁移账仍由后续单元建立；本Task不迁协议。F-132产品风险实现仍存在且禁用，新登记接口不得暴露该API、T-011最终复核仍待后续。CMD迁移时按真实新归属保留四项保护及其区分测试，不能简单删旧文件连同守护。未改封存A、不推送/PR/合并/远端状态。

证据说明、test-value gate及复现脚本见 [inspection README](../verification/evidence/task-02-inspection/README.md)。本Result不自批required快审、不提交Git。


## Independent Result Quick Review — R1

```yaml
kind: rks-review
verdict: approved
```

Reviewer: fresh independent `b_task02_review`；目标为Task契约r1、Result R1、base `e0604195b2ade73322984aec0b792604513d6270` 未提交变更，采用 `reviewing-work` Task Unit/Code。只读检查，未改文件或运行额外测试。

未发现阻止提交的局部缺陷。5测试当前SHA全部匹配最终清单，4个变异产品文件逐字等HEAD、Server/src无差异；旧断言未删除/放宽。四项RED分别命中error4额外回包、无权lease命令调用、DueSession残留、删除二验不拒绝；恢复GREEN有原日志。Safety最终前置证明实际投影变化非空再清空，并保留新generation。F132扫描实际12生产程序集、真实泛型方法身份/IL引用含async和构造器；同检测器拒坏调用、允许同名无关方法，准确为禁用而非修复，未运行风险API。

最终build0/0；App4631=4613+2准确既有Dimension失败+16skip；Host200/200。DFO原始62/61条、各实际程序集唯一control、原expected匹配、outside/exe/无帧0。新增7删除0与8365数量账如实，迁移账/其余测试/性能/完整Spec未冒称完成。

主控消费：批准只释放本Task检查点；随后按P1建立T-002剩余迁移账，不以本保护单元替代协议级CMD实施。
