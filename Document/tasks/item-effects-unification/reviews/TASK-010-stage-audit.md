# TASK-010 独立阶段检查

```yaml
kind: rks-review
verdict: approved
```

- 范围：TASK-010 Revision 1 的全入口请求身份、主结算账本、提交前 footprint、当前重放及旧机制关闭；仅本单位，不替代父任务最终审查。
- 对象：实现者正式停止 production/tests/文档/dotnet 后的当前工作树，含新增未跟踪生产及测试源、Result、checks、coverage 和正式 TRX。采用 reviewing-work 的 Task Unit 与 Code criteria。
- 独立性：只读源码、真实调用链及已有检查证据；没有运行 dotnet、修改生产或测试、提交或部署。仅写本审查文件。没有剩余本阶段阻断项。

## 源码与状态边界

真实 DI decoder registration → ClientMessageProductionRegistration → MessageHandlerRegistry → typed handler 获得精确18 Request CLR／20 selector1 wire；CommandHandlerRegistry 获得精确20 transactional main。001A 为 NPC/portable 两主命令，0204 为 coupon/fatigue 两主命令。3 XP 与 ReadReplay 仍受 selected-session 保护，明确不属于 request main。

完整 raw body 在 NetworkPlugins 的 decoder 前生成 SHA256 hex 与长度，保留 decoder 忽略尾部，未缓存可变 body。unknown 帧仍先 Observe。Ledger 使用原 physical context 的 CWT、同连接 Semaphore；selected recipient 在 await 前捕获，等待后验证同一 recipient，没有重新绑定角色。完整身份与128 all-frame ordinal 窗口明确区分 fresh 新操作、Committed 当前刷新、ConfirmedRollback 重试及其他拒绝；旧 fence 不因 fresh 清除。首次 handler 不跨全程持 lifecycle Shared，重放调用009的 Exclusive read→send 接缝。

45 在新 UoW 前 claim single main 并要求 captured session 一致。operation frame 与独立 AsyncLocal execution token 分开，closed inherited frame/token 明确拒绝，后续 readonly 不覆盖主执行。真正无 frame 的可信 direct bus 与网络入口缺失 metadata 拒绝分开。NoSelected 使用具名 refusal-only scope，保留原首次拒绝 ACK，随后选角也不能获得20主结算或 effects 权限，不建立付款缓存。

ApplyEffects 在事务内完成后累积 typed effect/change footprint；物理源、实际授予槽、装备 Before.Slot（含销毁）、账户/金币、fatigue/usage、quest/skills/XP、pet/premium 的分支逐项读取。Cooldown/Spirit/SeriaLuck 是具名无安全绝对消息类别；未知 pair 明确抛出。60 在 CommitAsync 前复制、Validate immutable projection，多个 Apply 可累计。58 在57仍持有、60返回后同步 Committed，早于 handler 发送；AbortAsync 确认成功才可记 ConfirmedRollback，typed uncertainty 为 Blocked 并沿008 exact fence。

六 ReplayGuard 类、DI 和 handler 分支源搜索为零；Lottery/Vending Completed/Remember 缓存及业务 sequence 参数已删除。典型设备、Recipe、Lottery/Vending 网络重复测试通过实际 decoder/dispatcher/bus/临时SQLite 驱动，保留经济、保护、销毁、wrap、晚 SQL 等断言。首次业务消息顺序和零晶体 event 策略保留，重复仅当前 absolute batch，不重放历史 ACK 或 earned XP。

## 施工问题关闭依据

1. 初版 closed frame 被视为无 frame、readonly 清共享 ActiveMain 两问题已修正。实际 inherited child 在 scope 关闭后拒付；独立 readonly child barrier 下父 main Freeze/Settled 仍保真。后者是 context-method 证据，later readonly 真 bus/SQL 与空槽000E ID=-1为另一独立证据。
2. closed 状态检查曾使58 Failed 再次抛出，绕过 fence并遮蔽 postdurable 原因。限定 ForSettlement 只匹配原 frame/main execution identity 做失败账务，不授予执行权限。真实 SQLite Gold100000→100001、commit participant 关闭 frame 的 Probe 保留 typed PostCommitPublicationFailure/closed cause，entry Blocked、exact physical/session fence 与再次拒付均断言；未将本 Probe 称为实际 socket close。
3. 连接 gate 等待测试从调用 flow 同步推进到 await 后替换 selected，证明旧角色 SQL 已扣一次、新角色零扣且 pending 不重绑。第二 main 在首 Committed 后拒绝，首条仍能当前刷新。same-ID 测试已改为新 context 加 distinct selected generation；仅替换 context、value-equal Session 继续 fenced 是正确行为。
4. Enchant replay sender barrier 后直接同步调用后一个请求至连接 gate；Lottery 首次真实 Roll entered 后同步 duplicate。已去掉仅 Task.Run 开始信号或瞬时未完成推断准入的证据弱点。
5. 原真实无 selected TCP 前置拒绝与新 ledger 兼容问题已修正。Forge/MagicSeal/Outworld 原 TCP case通过；capture-null 后等待→选择到达→main拒付另由 ledger/bus/SQL barrier证明。

## 已复核检查与证据界限

| 正式证据 | passed / failed / skipped |
|---|---|
| fresh 整方案 build | 0 warning / 0 error |
| TASK-010-application.trx 完整 Application | 2590 / 0 / 0 |
| TASK-010-protocol.trx 完整 Protocol | 1226 / 0 / 0 |
| TASK-010-networking.trx 受影响 Networking | 30 / 0 / 0 |
| core-registration-2 | 23 / 0 / 0 |
| migration-second | 110 / 0 / 0 |
| lottery-first | 38 / 0 / 0 |

专项与完整项目重叠，不累加。最初真实 Use RED 的 SQL source3→2→1、Expected2 Actual1仍保留；GREEN 使用有效 raw identity，实际当前000D typed batch 原context引用、数量2、无旧Use ACK且SQL仍2，并非非法 metadata 拒绝假成功。未知 footprint、非法 needs 和 late INSERT trigger 通过真实 SQL abort；开发失败历史没有标 baseline。最终 git diff --check 无输出。

新 raw metadata TCP 测试使用真实14-byte header（reserved11、sequence12）、实际 transport/decoder 与捕获 dispatcher，证明 ignored tail/empty hash及unknown ordinal；它不是 use 付款的 TCP全链测试，付款机制由真实 dispatcher/bus/SQL另证。本仓库 Observe→Decode/hash→Dispatcher→Ledger.Wait 前无 await，未取得实际 TouchSocket ReceiveLoop 版本源码，未声称 framework 永不并发，也未发现已证实真实重排缺陷。

checks 的第2个 API bullet 初读把001A/0204分支文字对调，controller已具名修正并复读；实际源、Task契约、注册集合及本审查上述映射一致。仅文档纠错，没有要求重跑生产检查。

网络 I/O 可部分投影，Committed 仍不允许再付款；准确 transport close复用008证据。选角 sequence reset、无 nonce 古老 raw 碰撞仍 Unknown。四组同库组合、动态 effects 注册/最终写入扫描、Host临时DB、全方案suite与集中人工验收留TASK-011。此批准不标 ClientVerified、父AC全部完成或整个重构完成；没有部署、运行库或客户端变更。
