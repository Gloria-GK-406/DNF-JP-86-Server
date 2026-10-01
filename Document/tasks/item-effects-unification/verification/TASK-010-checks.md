# TASK-010 请求结算账本自查与检查命令

对应 TASK-010 Revision 1。此单位只证明 CurrentImplementation；没有提交、部署、启动用户服务、修改运行库或执行客户端验收。

## 正式检查

环境：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`；dotnet 串行，fixture 为临时绝对 SQLite 路径、隔离 TCP listener。

| 范围 | 命令与证据 | passed / failed / skipped |
|---|---|---|
| 整方案 fresh build | `dotnet build Server/86JP.Server.slnx --no-restore -v quiet`；TASK-010-build-final.txt | 0 warning / 0 error |
| 完整 Application 项目 | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-build --logger 'trx;LogFileName=TASK-010-application.trx' --results-directory Document/tasks/item-effects-unification/verification -v quiet` | 2590 / 0 / 0 |
| 完整 Protocol 项目 | 同上替换为 Dfo.Server.Protocol.Tests 项目与 TASK-010-protocol.trx | 1226 / 0 / 0 |
| Networking 受影响范围 | 下列精确 filter；TASK-010-networking.trx | 30 / 0 / 0 |
| 核心行为与真实 DI 集合 | EffectOperationReplayTests、EffectSettlementRegistrationTests；TASK-010-core-registration-2.trx | 23 / 0 / 0 |
| 六设备入口／Recipe 与阶段核心 | TASK-010-migration-second.trx | 110 / 0 / 0 |
| Lottery／Vending | TASK-010-lottery-first.trx | 38 / 0 / 0 |

Networking 完整命令：

```powershell
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-build --filter 'FullyQualifiedName~UnknownInboundFrameIntegrationTests|FullyQualifiedName~ForgingFrameIntegrationTests|FullyQualifiedName~OutworldFrameIntegrationTests|FullyQualifiedName~MagicSealFrameIntegrationTests|FullyQualifiedName~GameInboundFrameCounterTests|FullyQualifiedName~PreparedBatchTests|FullyQualifiedName~ExactConnectionCloseTests|FullyQualifiedName~ServerMessageEncoderRegistryTests|FullyQualifiedName~ClientMessageDecoderRegistryTests|FullyQualifiedName~ExplicitMessageDelivery' --logger 'trx;LogFileName=TASK-010-networking.trx' --results-directory Document/tasks/item-effects-unification/verification -v quiet
```

阶段检查与完整项目有重叠，不累加为独立覆盖总数。最终完整 Application 包含最新 compatibility 和 same-ID fixture 修正。最后整方案构建在完整项目检查后再次执行，0 警告／0 错误。`git diff --check` 单独核对，exit 0、无输出。

## 真实 API 与源文件复读

- Abstractions 新增 `IEffectSettlementRequest`，Data `GameMessageMetadata.WithRawBody` 仅保留 immutable full-body SHA256 hex 与长度。NetworkPlugins 在 decoder 前对完整 body 求值，unknown frame 的既有 Observe 仍计入 ordinal；不是 parsed request 字段或 checksum 身份。真实当前 transport 14-byte header，reserved11／sequence12。
- 真实 DI 的 decoder registration→ClientMessageProduction→MessageHandlerRegistry 获得精确 18 request CLR／20 selector1 wire keys；CommandHandlerRegistry 获得精确 20 main transactional commands。001A 的 NPC／portable 两主命令及0204 fatigue／coupon 分支均覆盖。3 XP 与 ReadReplay 仍 ISelectedSessionCommand／Transactional，明确不属于 request main marker。注册检查断言具体集合，未以单一数量代替三项覆盖。
- `EffectOperationLedger` 是 singleton，ConditionalWeakTable 使用原 physical `NetworkConnectionContext` 对象引用建 Semaphore／bounded entries。selected recipient 在连接 gate await 前捕获，await 后精确 Demand；不重新绑定后选角色。首次 handler 跨完整通知受同连接 gate 保护，未跨 handler 预持 lifecycle Shared。Committed replay 直接调用009 Exclusive Refresh API，未 Shared→Exclusive 升级。
- key／identity 包含 exact SelectedSession、sequence、selector/type、full hash、length；128 窗口以所有 inbound ordinal 计，delta127可重放、delta128拒绝。Fresh 同参数正常新付款；合法65535→0与完整ushort圈可复用seq。无 nonce 的古老 raw 碰撞、选角后 sequence reset 仍 Unknown，不声称绝对识别所有历史请求。
- `EffectOperationContext` 的 operation Frame 与 execution token 分开。真正无 frame 才允许可信 direct bus；closed inherited frame／execution／refusal token 明确拒绝。readonly child 独立 AsyncLocal execution，不清共享 main identity。第二 main 在45/UoW前拒绝；later readonly CMD不能改变首个状态或 footprint。
- `Record` 是 exhaustive typed AppliedEffect/EffectChange visitor，检查 exact effect owner/session，累积实际物理源／目标槽、装备 Before.Slot（含destroy）、prepared／basic placement、账户／金币／fatigue／usage／quest／skills／XP／pet／premium 等 needs。Cooldown／Spirit／SeriaLuck 为具名无安全绝对消息类别。未知 pair 明确 throw，非 default drop；数组在 Freeze 时复制。
- 60 在业务 next 返回、CommitAsync 之前 Freeze＋Validate immutable ReplayProjectionRequest；多次 Apply 可累计，后续 readonly 不冻结主条目。58 在57仍持有、60已durable返回时同步 Committed，早于 handler NOTI。60 AbortAsync 确认成功才记 rollback；58 recursive uncertainty 记 Blocked并沿008 exact fence。ForSettlement 仅用原 frame/main execution identity做失败记账，closed标记不会先throw而绕过fence或遮蔽原cause，不能据此授予执行权限。
- `ApplyEffectsDomainService` 生产 DI 注入 operation context，应用完成后、提交前 Record。普通 trusted direct tests 没有 operation frame 仍支持；真实 dispatcher 缺失 raw/ordinal metadata 明确拒绝。
- 无 selected 的真实网络入口仍要求有效 raw/ordinal metadata，使用具名 refusal-only scope，允许原 handler 的首次拒绝 ACK；没有付款条目、不隐式降级 trusted，随后选择到达也不能发送任何 main command／effects。闭合 token 同样禁止继承旧 scope工作。
- 六 ReplayGuard 类／DI／handler branches 已删除。Lottery Completed／CompletedChange、Vending Completed／Remember／分钟缓存及业务 sequence 参数已删除。首次原 ACK／NOTI 顺序、zero 晶体过滤与业务费用概率保留；重放只投影当前状态，未缓存 receipt／奖励／可变名称／delegate，未重复 ACK／earned XP。
- `EffectRequestTransport` 以真实 body serializer＋生产 all-frame counter＋真实 DI decoder＋Dispatcher 驱动原测试，不是一律 Fresh 或复制 counter 算法。ShopSender batch 使用真实注册 encoder 与整组预编码，捕获原 context，保留故障注入。生产 actual registry／framecap／captured transport 仍由009及 Networking tests证明。

新增未跟踪 IEffectSettlementRequest、EffectOperationContext、EffectOperationLedger、EffectOperationReplayTests、EffectSettlementRegistrationTests、EffectRequestTransport 全部复读；45／58／60、dispatcher、ApplyEffects、metadata、raw transport、入口迁移源文件及实际 TRX 均复核。下列生产源扫描零匹配（rg exit1代表无匹配）：

```powershell
rg -n 'ReplayGuard|CompletedChange|Duplicate lottery request|Duplicate vending request|replay\.(Admit|Remember|CachedFor)|private.*completed' Server/src
```

## RED／GREEN 与实际证明层次

1. TASK-010-red.trx 保留真行为失败：真实 Dispatcher／Bus／SQLite 第一次 source3→2，Explicit I/O故障已发生在主提交后；相同请求再次支付2→1，Expected2 Actual1。GREEN使用有效 full hash/length，不靠 missing metadata拒绝重传。第二次进入009 current snapshot＋actual encoders typed batch，原context引用、000D当前数量2、无旧Use ACK、SQL仍2全部断言。
2. actual CommandBus／SQLite：closed inherited child拒付、连接 gate等待前capture后选角拒绝、第二 main拒绝且首 Committed仍可current replay、lateSqlite trigger确认abort后同fingerprint重试、未知effect/change及非法needs512在commit前SQL rollback、later readonly transaction不覆盖main且清空slot69的000E ID−1。
3. Postdurable Probe：真实SQL Gold100000→100001，non-fallible commit participant关闭operation frame，引发58 postcommit typed uncertainty。断言原closed cause、entry Blocked、exact physical/session fence及下一旧main拒绝。该fixture没有实际transportclose；其 precise close对象选择证明复用008 Networking evidence，不将本例说成真实socket断开测试。
4. Concurrent readonly scope用 context-method fixture，证明子flow不覆盖父 main execution；上面的 LaterRead 用真实 bus/SQL证明 readonly事务不改变已提交主状态。两种证据明确分开。
5. Lottery first真实 Roll entered屏障、同步duplicate进入连接gate await，最终random只1、付款／授予只1、一次原ACK＋一次current batch。Enchant actual replay batch entered屏障之后直接同步发送later request，证明后结算不能超车，不用Task.Run开始信号或瞬时False替代准入证据。
6. same-ID replacement必须原context新对象＋distinct SelectedSession generation；该测试证明CWT账本与physical fence隔离，old fence不清除。仅更换context但value-equal Session仍是同exact identity，session fence应继续生效。
7. 网络实际TCP metadata测试证明unknown帧ordinal、decoder忽略尾部完整hash、empty body独立hash、fresh/duplicate轨迹与header offset12。该测试用捕获dispatcher seam，不是结算bus；结算身份验证另由真实DI/SQLite tests证明。原无selected Forge／Outworld／MagicSeal TCP拒绝行为均保留通过。refusal-scope await后选角反例为真实ledger＋bus／SQL，未声称完整TCP race。
8. batch I/O失败保留Committed，later SQL seed7重放只发当前7。网络发送并非原子；中途断线可出现部分投影，不授予重付权限。整组编码前零send／captured transport复用009已有实际sender测试。

## 开发失败历史保留

- 真行为RED见上文。首次核心构建缺 request namespace、测试 fixture类型／InternalsVisibleTo／endpoint、xUnit2031 编译诊断均保留各 build txt，随后修正并fresh构建；不称行为RED。
- TASK-010-core-green-2.trx：13/1。trigger误挂 UPDATE item_slots，实际 store重建槽位使用INSERT；改为BEFORE INSERT后真实late SQL rollback通过。
- TASK-010-migration-first.trx：81/26。原测试重复ACK／cached-response断言与新current-only协议不同，另有反射读取、batch屏障、冲突错误轨迹需迁移。保留经济／保护／销毁／wrap／otherframe／currentqty／late SQL断言；修正为single firstACK＋actual current batch，冲突明确拒绝。后续110/0及完整Application通过。
- TASK-010-core-final.trx：20/1。same-ID fixture创建value-equal session generation，真实safety正确封锁；通过publish dummy selection建立distinct generation后按准确隔离语义测试。
- TASK-010-core-registration.trx：22/1。no-selected fixture Select1前容器重复注册；按既有fixtureAPI清理旧character containers，最终23/0。未将开发失败隐藏为baseline。

## 留给 TASK-011

四组缺失真实同库组合、动态effect注册与最终旧写入扫描、Host临时数据库组合、全方案回归和集中人工验收留011。本单位不新增道具效果，不关闭父任务或AC，不声称 ClientVerified。选角 sequence reset／底层 TouchSocket ReceiveLoop并发调度未取得直接证据；本repo Observe→Decode/hash→Dispatcher→Ledger.Wait前无await，按当前调用链保护，不发明新的调度系统。
