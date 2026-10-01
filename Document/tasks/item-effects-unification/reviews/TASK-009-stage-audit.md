# TASK-009 独立阶段检查

- 检查范围：TASK-009 Revision 2 的纯当前快照、限定生命周期 Exclusive 借用、当前状态投影和预编码 typed batch；不是根任务最终审查。
- 状态：approved（仅 TASK-009）。实现者正式停止生产、测试、文档及 dotnet 写入后，已复读最终新增未跟踪源码、任务 Result/checks、真实测试源码和最终 TRX；没有剩余的本阶段阻断项。
- 检查方式：只读实际源码、注册、调用链及已有证据；未运行 dotnet，未修改生产、测试、registry 或其他文档。仅写本审查文件。

## 检查中发现的问题与关闭依据

1. Appearance 快照最初直接使用 ReadCurrent 库存，未补 worn pet 的 Noti2CreatureMarker。实际 ExtendedUserState encoder 对 slot25/kind5 缺 marker 会抛异常。最终 reader 在 Appearance needs 下按同一 At 调用纯 SupportCreatureMarkerProjection.CompleteAsync 后冻结；真实 SQLite slot25 宠物经 registered Protocol delivery 和实际两条 NOTI2 encoder 成功，UID/slot 未维护变更。关闭。
2. 根检查发现 Quest TriggerValue 的 signed int 直接 checked(uint) 与既有通知保留 uint 位模式不符。最终使用 unchecked(uint)，-1 与 int.MinValue 的实际编码回归已通过；QuestId 仍 checked。没有借此改旧 full-selection 的独立 checked 路径。关闭。
3. 根提出 read 返回后的 Shared→发送时窗；独立复核了 Party MonsterDeath 按 recipient.Session 派 ApplyMonsterKill，以及向 secondary connection 发送 XP 的实际调用链。ReadReplay 返回释放全局57后，仅 Shared 不阻止另一 recipient settlement，不能依靠后继同连接 ledger 解决其他 reporter 的路径。最终只把 replay read/materialize/send 外围改 Exclusive；LeaseOwner 保留实际模式，Read45 可借用，已有 Exclusive 经 Shared child 可再次借 Exclusive，held Shared 明确拒绝升级，pending/canceled/closed 不算 held。真实 GoldProbe 的第二行为 RED 和同一 GREEN 已读。关闭。
4. 最初等待替代测试用 Task.Run 后瞬时 Assert.False，不能证明旧 session 已捕获。最终使用独立 flow 的 Exclusive holder 与确定 held barrier，再从调用 flow 同步执行 bus 至45等待；replacement 后原 read 拒绝且引用归零。关闭。
5. 首次完整 Application 的 RenamePetEffect.ItemUid 分类遗漏已保留为真实失败，不标 accepted baseline。根裁定仅补具名 singular world-instance 分类，与既有 AddPetStomachEffect 同一政策；generic inventory/pile 负例保留并增加 RenamePetEffect 正例。没有泛放全部 effect、改生产 UID 或重命名绕 scanner；fresh build 后完整 Application 2567 通过。关闭。

初读对 Entry.HasBlackDiamond 的时钟疑虑已沿生产 PremiumServiceDomainService.IsActiveAsync 复核并撤回：BlackDiamond 分支直接返回 configuration，不读 clock/expiry。最终没有据错误怀疑扩展该业务。

## 最终源码与调用链

- ICharacterInventoryEntryDomainService.ReadCurrentAsync 是 repository＋InventoryEntryProjection，未提供降级 Load 的默认实现。Opening 最终 receipt、Recipe Refresh、Select current-view 及 support current-view 使用 pure 接缝；selection entry 主角及 support 的 Load 仍保留 LegacyWornNormalizer。
- ReadReplayStateCommand 明确注册 Transactional，nonnull exact session 同时实现 ISelectedSessionCommand/IAccountCommand。45→57→58→60 保护实际 owner table 与同库快照，前后 DemandCurrent。依 needs 解析所需领域；Inventory-only 不请求 skill ledger。Skills/Quests 所需现有 Entry 聚合为只读，沿实际 tables/query 复核没有 Seeking.Synchronize、Recover、Reconcile、Ensure、pet Advance 或嵌套 bus。
- 快照拥有 private copied buffers；pet name、avatar jewel、skill extras/combos、character name/appearance/condition buffer 与集合冻结或复制，每次旧 DTO adapter 再复制。测试从原数据和 adapter 两方向修改，后续快照均不改变。
- 捕获一次 At。显式 At 的 fatigue、item usage period/cooldown、premium usage period、BlackDiamond rolling display 与库存 wire lifetime 均沿该值；跨日测试及旧 SQL row 不变断言通过。没有宣称冻结全服务器时钟或改变奖励估价。
- Refresh 要求 original physical context 引用与 exact selected session 匹配；外围 Exclusive 覆盖只读 command 至 SendBatch 完成，before/after exact recipient 检查。当前六张000D包含 Main14 virtual 的零值，金币 ItemId0 与当前空 touched physical slot 的000E ItemId=-1区分。occupied touched 不清空，account materials 为当前绝对余额。
- XP 为当前 level/total/SP/TP，ExperienceBonusLines.None；quest progress/offers 只读；premium 是完整 CMD036F，usage 使用既有 reset＋255 chunks。没有历史消费成功 ACK、旧扣除/获得 popup 或奖励增量。实际 encoders 继续约束 pet/skills/extras u8 与 inventory u16，avatar kind8 detail 保留，不自创 chunk。
- TouchSocket SendBatch 先核对 actual registry entry.Context 的 ReferenceEquals，捕获该 entry.Session；全组 registration、endpoint、实际 encoder/frames 和1MiB cap预检，frame复制后才第一 send。之后不按 ID 重新查 transport，Debug selector/type/name/frame 日志保留。旧 SendExplicit required/best-effort 行为保留。实际 encoding-time replacement 仅发捕获旧 transport，不误投同 ID 新 Session。

## 实际证据与界限

- TASK-009-red.trx：先 reset 已实际发送、后 encoder 抛错，零 frame 断言失败；不是新 interface 编译错误。
- TASK-009-ordering-red.trx：真实 Production CommandBus、45/57/UoW60、SQLite carried gold 与 pre-send barrier；旧 Shared 允许 GoldProbe commit，要求排队断言失败。该测试是实际 selected settlement 的门/SQL证明，**不是实际 Party XP wire 竞态实测**；Party XP 的适用依据是上述实际 caller/marker 链。
- 真实 SQLite read barrier：character inventory 已读后外部连接改 gold/account cube，同一 snapshot 仍读两个旧值；下一次 fresh read 才读新值。late read failure、wrong owner、stale/replaced session、未知 needs/非法 physical slot 均拒绝或保持资产。
- 真实 legacy worn slot24 snapshot read 不改 SQL row；selection Load 才移动到25且 UID保持。真实 equipped pet slot25 Appearance 从 reader→delivery→实际 encoder 的补 marker 路径已验证。
- Protocol 测试使用实际注册 encoder；Networking 测试使用生产 registry/sender/ITcpSession proxy，覆盖晚未知 encoder、endpoint、actual oversize、pet256/skills256/extras256/avatar缺detail、buffer reuse、transport replacement 与部分发送故障。网络 I/O中断可以留下部分当前投影，没有网络原子性声明。

停笔后再次直接读取最终 Counters（有重叠，不累加为独立覆盖总数）：

| 检查 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| 完整 Application | 2567 | 0 | 0 |
| 完整 Protocol | 1226 | 0 | 0 |
| Networking 相关 sender/registry/counter/closer | 21 | 0 | 0 |
| snapshot＋lifecycle gate 专项 | 23 | 0 | 0 |
| 保留的首次完整 Application | 2566 | 1 | 0 |

两个故意保留的行为 RED 均为0通过/1失败，最终 GREEN 见上述实际专项与完整项目。最后 fresh solution build 为0警告/0错误；本检查只读 git diff --check 退出0。测试临时绝对 SQLite 与既有 PVF 环境的证据不等于实机验收。

## 后继边界

本阶段可接受。CeraShop receipt/PartyMemberStatus 仍使用 entry Load，Sort 与 MoveItemSpace 等旧业务不在本单位迁移边界；不宣称全仓库 Load 或所有 notification 已统一。

TASK-010 尚需 raw request fingerprint、commit ledger、effect footprint 收集和历史 guards 退出；第一次执行业务 handler 不能预持 Shared 后再调用 Exclusive refresh。TASK-011 仍需最终遗漏扫描、全方案检查与集中人工验收。本意见不宣称父 AC-007 完成，不替代根最终独立 review，不包含 commit/deploy/运行库/PVF/client 操作。
