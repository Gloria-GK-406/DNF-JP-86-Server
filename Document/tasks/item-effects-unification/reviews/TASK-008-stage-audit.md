# TASK-008 独立阶段检查

- 检查范围：TASK-008 的会话生命周期、不确定结果阻断、精确关闭、pet 丢弃、kill/clear claim publication；不是根任务最终审查。
- 状态：approved（仅 TASK-008）。实现者正式通知停止全部生产、文档及 dotnet 写入后，已复读最终源码、任务结果与 fresh TRX；没有剩余的本阶段阻断项。
- 检查方式：只读源码、已有 TRX 与任务契约；未执行 dotnet，未修改生产文件、registry 或其他文档。

## 已发现并通知实现者的问题

1. PartyDeath 原先在 current view、skill mirror、KilledAsync 多次 await 后才捕获 recipient；最终版本把 Capture 移至每个 frozen recipient 的首次 await 前，并在 view/skill await 后 DemandCurrent。源码与最终 Protocol 测试已复读，关闭。
2. Lottery/Vending generic catch 原先仍吞递归 uncertainty；补正过滤器时两处单行 catch 被正则改成非法 C#，已即时通知实现者，最终源码手动改正，全部范围内正常 catch 递归排除 Contains。Fresh build 为零警告、零错误，关闭。
3. 根控制器发现的 BlockAsync await-before-fence 时窗：最终版本先在同步 lock 中设置 exact-session 与 CWT 物理 context fence，后取得生命周期租约。TryLog 保护日志异常不阻止安排 exact close。实际 SQL durable、Exclusive barrier 与 logger failure/close callback 测试源码及最终运行证据已复读，关闭。

## 当前源码检查

- 当前 23 个范围内命令均有 ISelectedSessionCommand；Order45 首次等待前 Capture，借用/取得生命周期 Shared 后 DemandCurrent，scope 覆盖 50/55/57/58/60。58 位于 57 内、UoW60 外，递归 uncertainty 出口阻断后原异常传播。Finish 的 Session 已贯穿 QuestCompletionDomainService 的 EffectExecutionContext。
- Dispatcher 在 handler lookup 前检查原物理 context fence；无 login authority 时也建立包含 fence 的 authorization predicate。CWT 使用引用身份，不把相同字段的 replacement context 当成原连接。
- TouchSocket exact close 在 registry TryGet 后 ReferenceEquals(expected Context)，捕获该 entry.Session 后 detached close，不再次按 ID 查找。Close callback 不被当前 handler await。
- DiscardExact 仅移除匹配 session 的 entry、roomRewards 与 projections，不调用 Advance/Gain/数据库；Leave 没有匹配 entry 时立即返回。Pending Prepare 比较 expected entry，discard 后 Commit 不重新创建旧 entry。

## Pet clock 并发疑点的实际调用链

CreatureClockService 在同账户 SelectedSessionTransitionGate Exclusive 内调用 AdvanceCreatureClockCommand；ProtocolModule 将该命令注册为 Transactional，再经过全局 Order57 串行门。范围内效果命令的 Order45 Shared 覆盖整个 UoW 与 Order58 discard，因此生产 timer 不能在这段保护期间并行写回旧 cached stomach。该结论来自实际 caller 与注册，不依赖外围 fake；没有据此扩展豁免的 pet clock/growth 重构。

MoveItemSpaceSlice 还有两处 AdvanceAsync 调用，属于范围外库存移动业务的既有 transaction 路径，保持范围外分类；本检查未宣称所有直接 runtime 调用都受范围内 marker 保护。

## 已读取的开发证据

- TASK-008-red.trx：1 个实际行为 RED，失败原因是 durable XP 后第二次同 session 执行未拒绝。
- TASK-008-xp-green.trx：10/10 通过。此前 xp-dev 的 8/10 与 2 个 PVF_ARCHIVE_PATH 缺失失败保留在原记录中。
- TASK-008-claims-dev.trx：52/52 通过，仅作为 Protocol claim 分支证据；不能替代真实 SQL durable 证据。
- TASK-008-safety-dev.trx：37/38，唯一失败为 replacement context 已继续到未注册 ChannelConnect 后抛 KeyNotFoundException，而测试期待 InvalidOperationException；开发失败保留，最终修正回归通过。
- 实际 pet 测试把 SQLite durable stomach 改为 80 后 Discard/Leave，检查 80 保持及旧 session 不删 replacement；已包含于最终 Application suite。

## 停笔后的最终复读与结论

独立复读了新增未跟踪的 SelectedSessionSafety、Order45/58 及 exact closer 测试，现有 Dispatcher/authorization、Finish 实际 effect context、23 marker/Transactional 注册断言、pet discard/Leave、全部范围内正常 catch、solo/party/clear durable/publication 分支。PublishActorDeathAsync 统一包住 actor publication 的 null 与 throw/OCE；有 paid recipients 时逐个使用首次捕获的 recipient 阻断，未支付 validation 分支保留原异常。Participant false、clear null/OCE/current progress missing 的原 claim 不释放为可重付。成功 claim 后发送失败沿原语义保留，不重新支付。

Networking 测试使用实际 registry 与 production closer：同 ID、同字段的替代 Context 不能被旧 Context 关闭；安排关闭后 registry 替换也仅关闭当时捕获的旧 Session。CommonExperience 使用真实 CommandBus/shared executor/SQLite 证明 durable XP 后 unknown 拒绝第二次支付；Protocol claim fake 的分支证据与该真实 durable 证据保持区分。

Fatigue 最后的补项实际调用 FatiguePotionHandler，使用 real Shop/CommandBus/SQLite，成功取得 outer Exclusive 并借用 Order45 租约，消费后 source 从 2 变为 1，返回成功 response；没有继续依赖直调 Domain 假称实际 handler 已覆盖。

最终实际读取以下 TRX Counters（各筛选有重叠，不相加作为独立覆盖总数）：

| 检查 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| Application affected | 702 | 0 | 0 |
| actual Vending | 12 | 0 | 0 |
| Protocol 完整项目 | 1218 | 0 | 0 |
| Networking Runtime + GameLoginIntegration | 23 | 0 | 0 |
| Persistence Database | 17 | 0 | 0 |

Fresh solution build 日志为 0 warning / 0 error；本检查只读再次运行 git diff --check，退出 0。未自行运行 dotnet 或修改生产代码。

本阶段可接受，没有剩余具体 production 缺陷或证据阻断项。后续统一 replay 与最终整合仍沿既定 Plan 执行；本意见不等于根任务最终 review，也不替代真人验收。
