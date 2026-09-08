# 移动后的快照同步：行为与整改证据

基准 `1d887c59`，2026-09-08 未提交整改结果。旧评审保留于 `reviews/change-review.md`。

## 方法与边界

先于实现的读者清点见 `reader-enumeration.md`：沿运行态目录读取点、命令/消息注册和返回值到编码器的传递追踪，基准有 16 个读取点、12 个文件，两处消费 CharacterInventory 的装备容器。
本清单从 `MoveItemSpaceRequest` → `MoveItemSpaceHandler.HandleMessageAsync` → `ResolveAsync` / `RefreshSessionAsync` → 目录写回与发送器继续双向追踪。命令处理器本身没有发送路径。

## 新增与保留的路径

| 路径 | 基准行为 | 当前行为及判据 |
|---|---|---|
| Moved 且可读会话、库存加载与替换成功 | 只在外观相关拖动后读取库存，不写快照 | 每次成功移动加载一次并 ReplaceExact CharacterInventory；副本选择随后读当前装备。实际 handler 的穿上/脱下消费链测试通过。PvP 参与者投影从同一成员读取，由代码追踪支持，未实机观察。 |
| Moved 但不改变外观（含包内移动） | 不读取库存 | 读取并更新快照，只发移动确认；P4 证明较窄门会漏掉该路径。即时外观帧读取 0..12、23 和契约兽槽 24，后续消费者读取整个装备容器。 |
| Refused / Unchanged | 不做外观读取 | 不刷新快照；拒绝路径有测试，Unchanged 从相同 Moved 门直接排除。 |
| 会话缺失，或 TryGet 已发现代次不匹配 | 不构造外观，已有移动确认保留 | 不变。 |
| 库存读取失败 | 外观相关拖动只发成功确认并记日志 | 同样隔离；现在非外观拖动也可能进入读取失败路径。 |
| TryGet 成功后、加载期间条目被移除或发布不同代次 | 基准没有 ReplaceExact，不会在此拒绝外观 | 新增内部 InvalidOperationException，记录日志，只发已提交移动的成功确认，外观帧被抑制；该新增发送限制是有意保留的，不能把失效会话继续当作可发布状态。测试检查 ErrorCode=null、MoveValue=1、单帧及日志异常。 |
| 同代次的其他 ReplaceExact 与本次读取交错 | 原有写点整体覆盖 | 新增移动写点也参与整体覆盖；不会因同代次内容变化而抛异常。见并发限制。 |

## 未改动成员与并发限制

无交错的刷新只替换库存，另外八成员保留读取时的对象；位置持久化标志由目录 entry 保留。测试使用真实 SelectedSessionRuntimeDirectory，检查八个对象与 true/false 两种标志。

`ReplaceExact` 在锁内仅检查会话代次，随后整体替换 Result；它不是 compare-and-swap。`RefreshSessionAsync` 在 LoadAsync 前读取 state，在等待结束后写回：其他写者在此期间更新的等级、任务等可能被旧 state 覆盖，旧六处也可能反过来覆盖刚刷新的库存。一次 `with` 不能证明其他读者在并发情况下不受影响。

命令门只覆盖 SendAsync 内部：`AccountExecutionBehavior` 的租约在命令返回时释放，五个旧消息 handler 和 Move 的刷新都在命令外；入场生命周期门未被 Move 共同持有。NetworkMessageDispatcher 没有会话互斥；NetworkPlugin 等待当前 dispatch 不足以证明不同 TCP 回调串行。这里只记录代码推导风险，不声称已实机复现。

沿用评审 BF-7 允许的“bound it or list it”处置：记录风险并去掉绝对保证。本契约明确不改旧六个写点；全面统一原子更新策略属于后续边界，本次未新增接口或改动其行为。独立复审仍需裁定这一处置。

## 新增验证与旧判据的授权变化

`The_session_copy_is_left_as_it_was` 改为 `The_session_copy_follows_the_move`，从“不替换”转为检查当前装备，直接对应本次获授权的库存同步。其余既有行为期望未反转；失败确认测试新增成功码与日志检查。构造整个 result 但复用同一成员对象与 with 等价，不属于错误；只有重建成员对象才违反所验证的身份保留要求。

新增消费端验证通过真实 EnterSelectDungeonHandler：刷新前旧衣服、穿上后新衣服、脱下后空装备。另有一次库存读取服务两条消费路径的计数，以及真实目录的位置标志测试。它们是交接后对既有实现补的证据，不冒称先红后绿的 TDD。第一次编译失败为测试命名空间遮蔽（CS0118，继发 CS0738），已用类型全名修复；日志 `Document/focused-initial.txt` / `focused-after-harness-fix.txt` 保留。

## 可重跑的变异证据

`Document/run-mutations.py` 保存逐次精确编辑；每次先以 binary I/O 备份，结束立即还原并 SHA-256 校验。未用 git restore/stash/checkout。完整输出分别为 `Document/P1.txt` 至 `P4.txt`，机器可读明细为 `mutation-results.json`。

| 变异 | 精确改动 | 失败方式 |
|---|---|---|
| P1 | 在 with 中新增 `CharacterEntry = state.CharacterEntry with { }` | 1 条：Only_the_inventory_member...，Assert.Same 对象身份不等；不是构造异常。 |
| P2 | 删除唯一 ReplaceExact 行 | 6 条：副本选择穿上后和一次读取测试的界面装备 Assert.Equal 不等；快照次数测试及非外观移动测试 Assert.Equal 不等；成员测试 Assert.IsType(null)；拒绝替换测试 Assert.Single 得到两帧。精确名称与堆栈在 P2.txt。 |
| P3 | catch filter 改为只在 cancellationToken.IsCancellationRequested 时捕获 | 2 条：RecordingRuntime.ReplaceExact 的 InvalidOperationException、InventoryThatRaises.LoadAsync 的 InvalidDataException 逃逸，均为预期注入失败。 |
| P4 | Moved 门追加 ChangesWhatTheCharacterLooksLike(message) | 1 条：A_move_that_changes_nothing_drawn_still_refreshes_the_copy，Assert.Equal 期望刷新 1 次实为 0。 |

还原后的定向验证 17 通过、0 失败（Document/focused-final.txt）。4 次变异均 exit 1，失败身份已记录，无编译错误。源码还原哈希：`93abc8df226df8bd06bf986891769bcccc37cefbfae033e6ef1653ea4c4da4a9`。

## 被本次改变否定的记录

- MoveItemSpaceSlice 旧“八处且无移动刷新”注释：实为六处生产调用，本次七处；过程解释移至本任务，源码只保留现行原因和并发限制。
- AGENTS.md：调用点总数与清单改为七个；记录库存同步、失败隔离与并发限制。
- DungeonSelectionSlice 注释：不能再声称与入场副本逐字节相同；说明使用后续已刷新的库存，其他字段未在这里重算。
- Docs/CharacterEntryFacesInventory.md：保留历史调查，开头补实现已查明的状态。
- protocol/game/client-to-server/0x01E4.md 与 traces/a21-tutorial-ending-20260826.md：保留旧帧记录，开头标注历史“无局部更新/少两帧”现已不准确；Tutorial 实际发送 AvailableQuestListMessage，Move 新增库存维护，不宣称完整 USERINFO 已实现。未改变布局或抓包置信度。
- reader-enumeration.md：明确基准日期和版本、Move 行属于基准状态，并纠正十处入场使用者的分布。
- spec/plan：补 AC-005 的证据映射、正确异常窗口、P1 精确变异及并发限制。

未验证项：真实 PvP 画面、同连接 TCP 调度串行保证、并发整体覆盖消除；本次不覆盖 #17/#18，也不发布或合并。
