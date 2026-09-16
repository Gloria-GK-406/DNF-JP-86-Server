---
kind: rks-completion-evidence
verified_at: 2026-09-16
baseline: 1677e4e2
---

# Root Completion Evidence

- Release：`dotnet build Server/86JP.Server.slnx -c Release --no-restore`，0 warnings / 0 errors。
- Application Tests（真实 `PVF_ARCHIVE_PATH`）：1488 passed / 0 failed。
- Protocol Tests（真实 `PVF_ARCHIVE_PATH`）：926 passed / 0 failed。
- Networking Tests：65 passed / 0 failed。
- 其余最近顺序验证：Launcher 63、Pvf 60、Host 17、Infrastructure 314 passed / 2 intentional skipped、LauncherApi 50、Persistence 353。
- 汇总：3336 passed / 2 intentional skipped / 0 confirmed failures。
- `QuestClearVerticalTests`：正常、picker replacement、picker departure 三种真实 SQLite 时序均在最终任务物品提交后打开一次共享 clear；helper 收帧，旧 picker/replacement 不收旧局帧。
- `DungeonCardPresentationCoordinatorTests`：可控 2 秒/4 秒、manual-first、auto-first、双人同列竞争、未提交 reservation 冲突、离场和 replacement 均通过。
- 翻牌事务：durable commit 前共享 selection 不可见；失败回滚并可重试；prepared actor 离场不允许幸存者窃取 reservation。
- `git diff --check 1677e4e2` 与当前差异检查均通过。

验证期间先后出现过既有 SQLite 测试的偶发 `ObjectDisposedException`（NpcShop/QuestLog 初始化路径）；对应测试隔离复跑通过，Application 全量随后 1488/1488 通过，堆栈未经过本任务变更。此项按环境/连接池偶发记录，不宣称由本任务修复。
