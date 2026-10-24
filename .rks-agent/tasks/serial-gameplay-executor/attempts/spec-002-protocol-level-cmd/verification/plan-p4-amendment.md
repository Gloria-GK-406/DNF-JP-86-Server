# Plan P4 amendment：CollectionBox 首次成功 batch 的最小方法闭合

## 授权、目标与输入身份

主控以 fresh governed Plan 作者分派本次有界修订，使用 `writing-plans/SKILL.md` 的既有 `task-units` 路由。工作区唯一为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`，读取源码 HEAD `dba3ede68e15f892afd35534db9fed7ed00d04a8`。根 Contract r4、子 Contract r1和Spec baseline `10a7218a779493494b138bd6b43cc0fc1a84aa6a` 不变。

写入授权仅三路径：`plan.md`、本记录、`verification/evidence/plan-p4-amendment/plan-p3-before.md`。原P3在任何Plan编辑前按bytes保存并校验52018 bytes/SHA；未重建早期产品before。其余Spec、reviews、routing、Task/Result、ledger、根/A、产品/tests和旧证据均只读。没有创建TASK-20，没有native/build/test/probe、stage/commit、版本、remote、部署或cleanup。本记录是方法与静态依据，不是本作者自批或运行验收。

实际读取的受影响输入如下，路径相对子B目录：

| 输入 | bytes | SHA-256 |
|---|---:|---|
| `spec.md` | 32948 | `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05` |
| `verification/spec-b3-amendment.md` | 10325 | `8cdcbb991a4ccbe202a965bcac8433e6ee816804a8ba9a3b4519c007c2cb89b2` |
| `reviews/spec-b3-challenges.md` | 7006 | `d61c61fa5f8aad20a3518b45619878b38927027a9e280d3da881f5750cf80fac` |
| `reviews/spec-b3-review.md` | 8194 | `42d37ed6073dd53a1904a77e8b847501c5d468f19bcc17e737c42fd39c1c4a52` |
| `verification/evidence/plan-p4-amendment/plan-p3-before.md` | 52018 | `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519` |
| `plan.md` | 61551 | `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7` |

B3 challenge为0 high/0 low，匹配独立Spec review verdict为approved。作者实际消费其结论及证据层级：Spec批准第三具名旧出口的WHAT，未批准P4、下一Task或产品；成功Withdraw动态缺口为nonblocking residual。不能用挑战计数或主控摘要替代实读。

治理来源实读根TaskContract Goal/Scope B/SC-13、子TaskContract Goal/Authorization/SC-13、根multi-spec-map A seal/B激活持续约束、父Spec RQ-023(c)/024；当前AGENTS的分层/协议唯一接线、CMD显式请求/注入面、Main/footprint/来源及默认失败/测试身份规则；以及ArchitecturePhilosophy、RefactorPreferences相关结构约束。工作区无 `.codegraph/`，依仓库约定跳过CodeGraph，使用精确路径静态读/rg定位，未索引或改变项目。

## 原方法冲突与实际源码

| 实际位置（相对工作区根） | 本次方法必须解决的事实 |
|---|---|
| `Application/Effects/ExplicitEffectRequest.cs:54–59`（以下Application简称`Server/src/Dfo.Server.Application`） | DemandRegistration的三元判定将非SettledRequiredExplicit一概当作replay，不能表达首次成功batch；只加enum会继续拒绝或错误借replay。 |
| `Application/Commands/CommandRegistrationAttempt.cs:7–38` | 仅原两个exit，Enter将单一expected与token.exit比对；已具备request、state、exact connection、entered/Completed与encoder cause。扩展须复用这些约束和现有opaque接口，不造第二凭据框架。 |
| `Application/Execution/Outbox/OutboxAdapters.cs:159–183` | 带token的RegisterBatch硬绑CommittedReplayCurrentStateBatch，显式required固定另一exit；来源章只在实际EncodeMessage/EncodeBatch窄catch。batch核与最终Register在encoder前后，非encoder失败不能混入。 |
| `Application/Commands/CommandHandlerRegistry.cs:62–70`；`Protocol/ProtocolModule.cs:326–333`（Protocol简称`Server/src/Dfo.Server.Protocol`） | 注册验证未知/重复许可与typed effect绑定；CollectionBox现仍legacy fine Transactional。新通用phase仅在ProtocolModule给其精确whole/message声明，Application不得识别CollectionBox CLR。 |
| `Protocol/Game/IGameCommandOutbox.cs`；`Abstractions/Networking/ICommandMessageOutbox.cs` | 现有token batch/explicit重载已经能透传同一opaque对象；Protocol负责typed转换，Abstractions无需引用Application enum。无需新增public phase参数。 |
| `Protocol/Game/CollectionBox/CollectionBoxSlice.cs:18–38,50–58,103–137` | 真正decoder有Select/Put/Withdraw/Extend四wire形状；minimum为Select CollectionBox、其它Inventory\|CollectionBox。fine中DemandActive→Execute→RecordSlots；handler前后current、四named catch、成功result非null后库存→Full/selected重复→ACK且一次exact物理batch，拒绝另走batch。一般SQL原来未catch。 |
| `Application/Characters/CollectionBoxDomainService.cs:70–124` | Withdraw成功确有物品到达动作，然后共用成功batch；不能凭共用路径把未跑的成功Withdraw动态注错当已测。 |
| `Application/Commands/CommandBus.cs:86–135`、`CommandRequestContext.cs:73–162`、`Transactions/CommandWorkUnits.cs:160–197` | 已有先drain、Close、scope cleanup、终判和独立失败事实收口，及scope同对象再抛区分。P4须保持，不凭Main已Committed提前raw/fence，也不削弱默认typed/所有参与者保护。 |

这里前述简称仅为表内路径缩写，源身份在文末完整路径表。P3的commit-only结果、KnownStorage来源/GuardFailure、RequireRollback(cause)独立两error保因、默认Write/ReadOnly sticky均已实读，未改其方法或旧历史记录；CollectionBox不需要把原未catch普通SQL强恢复为拒绝。

## 最小闭合方法与责任

1. **阶段与注册。** 保持原`SettledRequiredExplicit`和`CommittedReplayCurrentStateBatch`，新增Application通用机制名`SettledInitialBatch`；ProtocolModule只给CollectionBox whole及精确CollectionBoxRequest声明。注册器对新项保未知/重复fail closed与effect绑定，不追加Application具体协议白名单。请求签发仍检查实际注册许可、本request/job健康未closed、exact target、全部operation/unit清理结束及Main实际状态。
2. **状态穷尽。** DemandRegistration改穷尽switch：共同要求exact physical、Entry.Committed、Main非active；原required和新initial均非replay且settled，replay仍IsReplay且普通Write已确认提交。未知值不走else合法分支。whole的成功`result != null`属于真实用例分支，不让Application辨业务成功，不造全局success receipt。
3. **透传与adapter调用矩阵。** 现有opaque attempt携phase，从typed Game重载经现有Abstractions接口原对象传至实际adapter；无需public phase参数。内部Enter分具名batch/required-explicit入口（共用private校验可留局部实现）：batch只认replay/initial，required只认原required，保single-call、当前owner/job、state、exact physical target。替换batch硬绑replay，不改变无token普通重载；RequiredExplicit不能借batch，新initial不能借replay。只声明enum/metadata不算闭合。
4. **真实encoder来源不扩。** 仅actual adapter的EncodeMessage/EncodeBatch窄catch标原cause并原样throw；typed转换、callback、lookup、current/guard、缓冲或Register前后都不能盖章。所有已启动work、drain、unit/scope cleanup完成后，仍只有同一个原cause、无typed/额外独立失败，才raw、Committed、无新增fence并保source-close/current replay；独立阶段同对象再抛仍撤销raw。异常类/文字、业务Func或replay许可不能代替该AND条件。
5. **CollectionBox真实消费者同单元。** 原fine一个Transactional映为一个显式Main Write，mandatory footprint改`unit.Footprint`，保持DemandActive→Execute→RecordSlots及minimum needs。四named catch/拒绝码、前后current、空actor和ordinary SQL原语义保持；需正常拒绝时沿既有RequireRollback(cause)，不吞其guard、不扩StorageFailure或commit-result恢复。成功Main/Tx/Freeze/Settled及unit结束后，原库存/clock/Full/selected重复/ACK组帧顺序保持，只有实际成功分支在组帧后签initial并对原physical一次batch；拒绝不获许可。非空whole返回形状、驱动和白名单由临近Task决定，必须满足IProtocolCommand<TResult>:notnull。
6. **不增加请求暗道或执行模型。** 不引入request AsyncLocal/DIcurrent/job slot/单例字段/从环境Tx取请求/任意Func豁免、第二GameplayJobHost、第二总线或第二credential框架。§5构造器允许面不变，既有同provider Registering/actual adapter/GameplayOutboxes与inFlight/encoder观察链复用，CMD/NOTI/Required/BestEffort和Compose禁用/F-132不变。

这是high-level方法决策，内部方法拼写、whole局部结果值和详细fixture实现是可安全延后的局部选择；没有需要改Spec WHAT或请求用户语义决定的未决项。若真实旧oracle否定边界或出现更宽需求，临近Task先报告主控，不静默扩大。

## 旧证据层次与后继验证义务

本轮没有实跑。`CollectionBoxNetworkSettlementTests.cs:31–50,118–145`的Select/Put手造request直达dispatcher、只断言IOException类型；持久结果及后续current replay是Committed/no-new-fence间证，不能声称已有causeRef、真实entry/原Ordinal、直接fence/source-close观察。`:148–172`覆盖Put帧序与Withdraw拒绝；成功Withdraw仅静态共用路径。`:66–109`的physical reuse hook没有断言实际替换触发，须保case并补真实触发事实反例。

`NpcShopPurchaseTests.cs:575–595`的FailBatch由同provider RecordedOutbound.Registering的inFlight进入FaultingEncoder；`RecordedOutbound.cs:337–389`保wrapper调用及actual adapter，`OutboundRegistrationParts.cs:88–101`实际encoder抛错。不是physical transport；`RecordedOutbound.cs:423–427`的Discard不真实关闭socket。旧证据和这轮静态核实都不替代新oracle。

后继产品Task依Plan Verification先旧后新：

- 产品迁移前，以真正旧decoder Select/Put固定每case同cause的actual registration encoder oracle，保最小旧source/driver与production SHA、真实UTC/完整日志及原物理target；不重建假before。然后同provider新decoder/dispatcher/whole/Main/SQLite/outboxes对应复跑，直接验causeRef、真实同entry/原Ordinal、Committed、无新fence、失败batch未登记、source-close顺序和改变世界后的current replay、不重复结算/不原ACK。Withdraw可另补actual成功wire case；未跑继续标nonblocking residual。
- 相同IOException来自业务Full/clock/current/lookup/guard/Register、拒绝/其它协议/非许可出口仍默认保护；无/错许可、错phase/调用、request/job/physical target、closed/leaked/reuse及catch后write/return都要实际可区分。保原两许可完整正反例与mutation；新增真实provenance可编译行为mutant的RED和恢复GREEN，不能以编译失败或未触发fixture冒RED。
- 所有drain/额外独立unit/scope cleanup含同cause再次发生撤销raw、保各因和独立发生事实，默认typed/aggregate全参与者围栏；保P3来源guard/RequireRollback两error保因/default Write/ReadOnly sticky及所有既有共享保护。
- 一个仅静态、未演示的局部风险：Enter只Failed token.owner，而DemandRegistrationCurrent遇closed/job失配只throw。现ExplicitEffectCommandTests:357 PreviousAttempt是未处理throw，:379 liveforeign主要验owner-drain；未证明新request catch后实际写/成功return会被阻止。Task先真实同provider probe，若已由其它守卫阻止则保证据；否则在现有显式request/attempt/登记边界最小修正并由行为反例驱动。不得据此假定已证bug、加ambient/currentrequest通道或滥用self-wait fault API。这是B3原catch后保护的受影响验证，不是新Spec义务或A重设计。

共享core变更后需要新鲜完整Release build0/0、八项目full及原并行/5分钟deadline/Skip；原DFO74 expected＋七actual controls、任何额外intentional逐类/数、Golden15/Characterization10和display/status/Counter逐case增减闭合。当前接手起点仅Task19库存8828＝8808 pass＋2 exact old Dimension fail＋18 skip；三个最新full7028＋其它Task15库存1800＝8828，不能冒称本轮或当前八full。P3历史Verification的8644/下一Task15原文显式标为当时起点，当前P4使用8828。旧证据与已接受Task01～19不回写，不要求尚不存在的Task提供完成结果。

## 修改位置与门后顺序

Plan仅更新当前Planning Choice、共享§4/§5、必要T-004、对应Verification和当前Next Planning Boundary，追加P4本地修订；P3 commit结果段、P1/P2/P3历史修订与原“两出口”历史描述保留；§4将P3当时“两次FailAsync”的过期现状句对齐当前已经落地的一次最终收口，不改其保护要求。T-001～012其余路线、task-units/on-drift、17RQ/11AC、VC/gates、baseline、A和已接受Task01～19不变。没有伪推进routing/ledger或预建Task20。

P4 `state: ready`仅表示方法稿可供审查。写完停止全部共享写入→fresh有界Plan challenge→另一独立review绑定当前Plan/本记录→主控完整消费并更新routing→amendment-only本地检查点→主控指定临近Task详细规划和产品。不得用Spec B3批准、旧P3批准或本作者静态自查越门；没有新的用户权限问题。

## 本轮读取的关键源身份

以下是静态读取时源码SHA，不是旧产品native取证副本；源均保持未改。

| 文件（工作区相对路径） | SHA-256 |
|---|---|
| `Server/src/Dfo.Server.Application/Commands/CommandRegistrationAttempt.cs` | `9e2547994fc5de1bd24daf6a38dd03e2b89c89b9501bae3c3288125d048e84a8` |
| `Server/src/Dfo.Server.Application/Effects/ExplicitEffectRequest.cs` | `558746137a00a6a86d3f2790537ff5d3d9ddff731bc05b832a073367984c58fd` |
| `Server/src/Dfo.Server.Application/Commands/CommandRequestContext.cs` | `50fefc740334b04fbdabddd29a38f5f7516781c7f0b3b6239686a39d99052e1a` |
| `Server/src/Dfo.Server.Application/Commands/CommandHandlerRegistry.cs` | `b6504bcb45cac8d38553ba3f7b5077713406baef43f105921c459f50b7bd023e` |
| `Server/src/Dfo.Server.Application/Commands/CommandBus.cs` | `52f3d06ed636a5b87545a724473120705307f5239ee3cb33b8308c1219af9bf5` |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs` | `50336652fad2b84338641bd9243fb9fa48ada532982779f05c91124ce3ea4817` |
| `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs` | `2d701fcf3fa8d125555c886f89bb8eee12918e3c62352f1f19b8444dbbd867a1` |
| `Server/src/Dfo.Server.Protocol/Game/IGameCommandOutbox.cs` | `6186ed2fff50b8bc2bb11cf0108b6d47be66089a7a3bbe51192524b804c2269c` |
| `Server/src/Dfo.Server.Abstractions/Networking/ICommandMessageOutbox.cs` | `bb9056d9fd47cd38379dd302d0756e5f838d8f8d18038493d64b54d014ec8bd7` |
| `Server/src/Dfo.Server.Protocol/Game/CollectionBox/CollectionBoxSlice.cs` | `77be2e3ce11344fc6531a2576fdd440cf20e6631c025c68f298175bc6a75ddb8` |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/CollectionBoxNetworkSettlementTests.cs` | `6900798ecb4a160ca873908567ff19ecdaf36e94300a140af579e54d1792f4a2` |
| `Server/tests/Dfo.Server.Application.Tests/Commands/ExplicitEffectCommandTests.cs` | `016ce6c96abae9488aed19080ccf078e9e485ab6c8b7991594e14e4bdf9988fe` |
| `Server/tests/Support/RecordedOutbound.cs` | `0a808005393fc54ab179ffeacdd7ae8654d402896a852a7913fa058d15f993a6` |
| `Server/tests/Support/OutboundRegistrationParts.cs` | `3d8cddb5c1435857a6b2c9920e6d02b2365acb4ea9f059626753dc8dc9155dda` |

本记录生成UTC：2026-10-24T16:56:28.388957+00:00。该时刻仅为文档落稿，不伪装旧oracle运行UTC。本作者交付后停止共享写入，等待主控发起独立门。

## 作者交付核对

```yaml
kind: rks-verification
decision: manual_required
```

精确声明：P4方法稿及配套修订记录已按三路径授权完成，供fresh独立门审查；不声明Plan已approved、产品已实现或测试通过。交付静态核对（shell均exit0）：B3四输入与原P3 snapshot bytes/SHA匹配；§1～3、§6/§7、其余T行、交接索引、最终集成及P3 commit/action方法和P1/P2/P3历史段逐字节保持；文末14项源SHA均未变；HEAD仍dba3ede68e15f892afd35534db9fed7ed00d04a8，index为空，Server/Tool无差异；plan的git diff --check无输出。作者写操作仅本记录、Plan和P3快照，未触其他制品。

Unverified Conditions：P4 fresh challenge及另一独立Plan review尚未产生，主控routing/amendment-only检查点尚未执行；全部后继旧/新oracle、guard探针、mutation、完整Release/八full及动态成功Withdraw仍未运行。本记录不把这些条件算pass或要求不存在的Task提供完成证据。作者在本交付后停止全部共享写入。
