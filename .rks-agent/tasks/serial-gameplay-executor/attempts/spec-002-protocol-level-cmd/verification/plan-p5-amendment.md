# Plan P5 amendment：Cera单协议消费既有确认回滚结果与精确买家保护

## 输入、权限和当前目标

本次是已独立approved的Spec B4 ready R2的受影响HOW修订。主控已完整消费B4及immutable CH-1、fresh独立approved review，授权本writer只改Plan并新建本记录和原P4快照。不是Spec/Contract修订，不是Cera产品或测试实现，不选择新的VC，也不批准本Plan。

唯一工作目录 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`；HEAD `f42efb55bedad71dc1196100e39255bc483ed72c`。Task28已committed done、required fresh结果快审approved，Task最终35298B SHA `9c8e5a720b8a887e114331b3ddcd4232ac3c67a43967dbf7aca7c5893e32d24a`。写前已有B4及其review等7个未提交路径均只读；本writer不修改routing、当前Spec/Contract、root/A、旧Task/证据或任何产品/tests/ledger，不native、git mutation或创建Task29。

| 治理输入 | bytes / SHA-256 |
|---|---|
| 当前 [Plan P5](../plan.md) | 71863 / `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| [原P4逐bytes快照](evidence/plan-p5-amendment/plan-p4-before.md) | 61551 / `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7`；与写前HEAD plan完全相同 |
| [Spec B4 R2](../spec.md) | 42632 / `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| [B4 amendment](spec-b4-amendment.md) | 13392 / `a73b9317a2be57767a73cf4937b4553fc80abb81f526a62b2f6ded1c2ca07d8b` |
| [B4 immutable challenge](../reviews/spec-b4-challenges.md) | 6366 / `f8859fbc613f6f199f62fb6398de3b9c486accb09ba00b8a739b1c93b0a7ea86`；CH-1在R1成立，R2已解决 |
| [B4 fresh独立review](../reviews/spec-b4-review.md) | 7624 / `19d7a2d0a5075f89858ab1c32b70d575fbabe350a4e62f45d8f7528eeb659848`；approved并精确绑定当前R2及原CH-1 |
| child Contract r1 | 8898 / `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |
| 当前routing（仍指B3/P4，主控门后更新） | 4751 / `db94843b5ececc4e27d3919e8159bf209c9a38332715fb4e0eb03308a69c6ff9` |
| Task28正式旧报告 / manifest | 8709 / `601a55e13b8da7bded7c8783c3bcea4f6cf66f0fa265438c3c95b62a2194d50e`；21161 / `dfb1dfd50d2ad029ae0402e3123b901bdd8aa4d7e51c0d3accbc52e01c9c6314` |

Spec baseline仍为`10a7218a779493494b138bd6b43cc0fc1a84aa6a`，不是当前Task HEAD。17RQ/11AC、VC-B1～B3、根/子Contract、A seal、controller/profile/task-units/on-drift和原gate强度不改；VC-B4的WHAT由当前B4拥有。B4 R1/原B3快照及所有旧challenge/review原文保持。本记录只承接已批准结果，不倒写Task28先前的旧参与者裁定，也不把Spec gate当Plan gate。

## 读到的实际接缝与选择依据

下列是本writer实际读取的当前源码/证据，路径相对worktree。声明可达与已动态证明分开，不以名称替代事务阶段。

| 源码/证据 | 当前事实与P5选择 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseCommand.cs`、`Server/src/Dfo.Server.Protocol/ProtocolModule.cs:725–726,979` | fine只有IAccountCommand，一次Transactional；旧handler编排一个fine，logical Game CMD0x0040。新whole只替这一个协议，不触其它来源/后簇 |
| `Server/src/Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseHandler.cs:25–70` | cart日志/session/online分支；try覆盖整个fine，业务拒绝及非取消异常映原reason或StorageFailure；成功Project及NameTagVisibility在try外。原ACK码4/11/1和投递顺序均须迁进whole |
| `Server/src/Dfo.Server.Application/Inventory/CeraShopDomainService.cs:33,101–203` | 多次DemandEffects；wallet/gold/限制/扩容/名字牌/权益/物品和receipt读在同事务，未自Rollback。不能用无足迹普通Write或临时legacy桥执行 |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs:63–81,104–128`、`Server/src/Dfo.Server.Application/Commands/ICommandWorkUnits.cs` | 当前已存在WriteResultAsync及封闭Returned/CommitRolledBack。NonRequest要求ProtocolNonEffect、Ordinary write、一个精确physical actor；现有恢复还要求真实KnownStorage/cause、ConfirmedRollback、健康request/current及无guard/uncertain。因此只增加具名消费者，不新增API/core恢复机制 |
| 同文件`:145–197`、`Server/src/Dfo.Server.Application/Effects/ExplicitNonRequestFootprint.cs`、`Server/src/Dfo.Server.Application/Commands/SelectedSessionSafety.cs` | Committed/Uncertain记该actor后默认FailAsync执行Fence/精确连接阻断/DiscardExact/关闭。沿B4明确新买家保护，不为保旧空集合改默认runner或足迹；普通成功不会调用FailAsync |
| `Server/src/Dfo.Server.Application/Commands/CommandBus.cs:73–132`、当前outbox最终收口 | body之后drain/Close request/scope cleanup，再最终裁定；已提交或单元外条目保其原序，失败关闭置后。C3映到新whole scope后可能已有消息，不可承诺零帧或手动撤条目 |
| `Server/src/Dfo.Server.Protocol/Game/PackageOpening/GamePackageOpeningCommand.cs` | 已有窄action try、RequireRollback(cause)、单元外按Returned/CommitRolledBack映拒绝的真实消费者；复用其机械边界，不复制效果Main/ledger到Cera |
| `Server/src/Dfo.Server.Protocol/Game/BuyItem/GameBuyItemCommand.cs` | 已有NonEffect普通买家声明NonRequest及显式metadata的真实接法；Cera保持单精确actor，不新增通用请求工厂/ambient |
| `Server/src/Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseCompleted.cs` | 原扩容快照→产品ACK→wallet→gold→inventory→pet→restriction→premium/state，与独立NameTag可见会话刷新保原先后；现有通知handler继续组包，不改成新raw出口 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/CeraShopPurchaseTests.cs`、`Server/tests/Dfo.Server.Application.Tests/Sessions/ExplicitEffectExecutionTests.cs` | direct fine/手造request旧fixture与domain强测试混合；原wallet/UID/宠物/权益/晚期回滚/旧selection/receipt不投替换等意图须保，不能为新wire形状删除断言。真实新whole和领域层证据需要准确分层 |
| `Server/tests/Dfo.Server.Host.Tests/GmCeraHttpTests.cs:280–291` | 三个真实HTTP grant与三次购买同时进executor、逐receipt和最终97810/三operation的强竞争用例；替换旧fine入口时不可降成SQL预置或假receipt |
| Task28`report.md`、`final-01/` | A/B是真action19/1811和实际SQLiteCommit19/787的已接受before；C1/C2/C3是真domain/persist/旧scope加具名controlled故障，不是生产事故；实际旧ACK/no-fence/no-close须按B4有意改变 |

Cera只迁编排和旧fine入口。原参数式ICeraShopDomainService、规则/内容/存储和通知能力复用；没有需要先交付无人消费的接口/enum/core改造的依赖。如果临近实现发现当前API不能维持这些边界，应带实际缺口返主控和受影响gate，不能在本P5“保行为”的名义下扩通用恢复或改机制。

## 方法和保护分界

1. **一次普通写，不是Main。** 由唯一协议CMD承接旧handler全部用例阶段，薄handler只入队。缺session/online的原直接拒绝留在不进入写单元的分支；其余构造本次精确actor、Ordinary+NonRequest声明，显式传本次Game metadata/physical context，再调现有WriteResultAsync。actor/current/footprint/runner设置不包进业务catch。domain仍拥有购买交易和规则，不把内容/SQL拉进CMD。
2. **action和commit恢复各在原归属。** 窄try仅原clock/domain；非取消/nonuncertain业务故障保存localcause，RequireRollback(cause)后正常返回，由whole在unit清理完后映旧拒绝/日志。RequireRollback本身的guard或typed拒绝不能同catch吞掉；normal返回拒绝不人为RequireRollback。真正Commit回值只由runner当前KnownStorage窄span签发，where/phase/cleanup条件不变，取消/guard/unknown/Uncertain/独立错误不得冒回滚恢复。Cera无Mailbox自Rollback，不为它提前开新入口。
3. **默认失败和精确买家保护。** C1/C2/C3等默认不可恢复错误继续sticky/typed/aggregate、真实提交及cause引用；现有FailAsync对单精确买家Fence/连接阻断/DiscardExact/close，落实B4新增可见结果。不得把“正常拒绝无围栏”误解为拒绝ACK编码又失败后仍无保护；那时已发生另一个默认失败。也不将健康正常Commit当围栏理由。
4. **scope和出站保原机制。** 将旧fine scope观察对应到新whole真实DI scope，而非模拟body错误。已有条目按当前单元状态/登记顺序生存并在失败close之前处理；不因cleanup补StorageFailureACK、不提前flush/撤消息，也不加入Cera raw许可。精确保护不能误清/误阻断替换身份和无关物理连接；关闭生命周期正常引出的既有通知并非这种“误伤”。

P3四opening、P4 CollectionBox最小phase/opaque adapter、三个raw真实来源/默认对照、GuardFailure/RequireRollback两因保留、默认Write/ReadOnly、KnownStorage、scope/cleanup和Compose禁用均不变。P5只把一个已获B4授权的真实Cera消费者接到现有能力上，没有增加第四raw许可、普通Action恢复API、clearFailure或第二执行器/outbox。

## 直接验收与证据分层

- 以Task28已提交的same-provider真decoder七例作before，新probe/强测试在未来Task自己的新路径适配；不能为删fine后的编译回写历史探针。必须保完整NULL/BLOB、schema/列、事件及实际SQL阶段/同对象cause，原loaded程序集和source freeze关系及原native命令/exit。原捕获的内存transport/controlled failure/可能提前enlistSQLite观察影响继续披露，不称TCP或真实生产事故。
- A/B分别覆盖动作晚期已暂存后的SQL19/1811、domain先receipt且FK1/child>0后的真实SqliteTransaction.Commit19/787；前者是RequireRollback(cause)→Returned拒绝，后者是真CommitRolledBack。完整资产/事件回滚、原失败ACK1/no-fence/no-close及去trigger后仅一次新合法购买保持。normal refusal Committed原事实保持，不套Main/Ordinal/效果幂等。
- C1/C2沿原受控nonfallible和rollback双cause，C3沿新whole真实DI scope清理；保各cause/真实资产/Completion，不再映普通失败ACK或正常结束，直接核精确actor围栏/IsBlocked/DiscardExact/物理close/链。C3记录故障前消息和最终发送/关闭实际顺序，不把“无StorageFailureACK”写成“无任何帧”。用实际不同/替换身份的状态和物理连接证明不误伤，不能只凭Exact名字或一条静态调用。
- 既有direct-fine测试按意图分别迁真实wire/whole或领域+原显式事务test seam；不能把不能wire表达的旧session变成新协议字段、删掉原NoSession/rollback断言，或保legacy命令转发供test使用。Host HTTP竞争仍是真grant与真购买。局部fixture/whole非null结果/白名单在临近Task才收敛。
- 按实际变更做Release build0/0、受影响full/逐case数量与断言变化、DFO原expected/control、Golden15/Characterization10、source/runtime/pure family、Cera ledger闭合和全部原tracked/manifest/行尾检查。若触共享core/API则返主控并扩大八freshfull；无core变更不机械把小消费者当新core，但最终T-012的八full/74+7/性能接受线全部保持。没有本轮native、test或capture通过主张。

## 当前边界与待完成门

Task27已接受库存仍9014＝8994pass＋原exact2Dimensionfail＋原18skip，注册捕获为173routes/201commands（35Protocol、1Internal、165LegacyFine；137Tx/51Direct/9ReadOnly/4Workflow），46消息ProtocolCommand/127LegacyQueued、14telemetry，三出口owner保持。Task28只新增调查，没有更新这些数字或关闭Cera ledger。本P5引用这些已接受输入，不冒称当前新capture或八full。

修改仅限Planning Choice、§4 P5消费者与默认保护适用说明、§5该能力的消费限制、T-004、P5直接验证/旧P4历史标记、最终VC引用、当前NextBoundary和新增P5本地修订；其余共享决定、T-005～012粗路线、原P1～P4修订原文和既有验证不重写。原P4关于“只四opening”是本轮之前的受影响限制；P5只明确放行Cera一个消费者，不推至Mailbox等其他协议。

下一步必须fresh有界Plan challenge和另一独立Plan review；通过后主控完整消费Spec B4/P5、更新routing并amendment-only本地检查点，才指定临近Task路径详细规划/生产。本ready稿/本记录不自批、不创建Task29、不阶段性代替Spec002完成，不新增用户HITL。中文，不stage/commit/push/PR/merge/remote/版本/tag/部署/游戏/cleanup；Astra待审及用户明确收尾门继续有效。
