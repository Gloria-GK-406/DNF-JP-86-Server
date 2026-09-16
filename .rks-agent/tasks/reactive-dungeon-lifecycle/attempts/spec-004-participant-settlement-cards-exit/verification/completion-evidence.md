---
kind: rks-completion-evidence
verified_at: 2026-09-16
---

# Completion Evidence

- `DungeonRunDomainServiceTests`：未 `ClearOpen` 不可结算；参与者独立 exactly-once；共享 free/paid 列与 seat 快照；离场后 recipients 收缩。
- 翻牌事务测试：共享 selection 在事务提交前不可见；durable commit 失败时 selection 回滚且原列可重试；actor 在 durable commit 内离场时 prepared reservation 不可被幸存者窃取，旧 commit 只删除原 token。
- `DungeonCardSelectionProjectionDeliveryTests`：完整共享牌面投影到当前 frozen generation；库存和金币只发送给 actor；replacement 与离场者不接收。
- `DungeonCardPresentationCoordinatorTests`：手动释放 2 秒/4 秒阶段；manual-first、auto-first、离场、replacement、双人同列竞争和未提交 reservation 瞬态冲突均确定性通过。
- auto-free 瞬态冲突测试的尝试序列精确为 `[0, 1]`，共享快照不前进时也不会热循环；最终只领取一次。
- `SelectCardResponseEncoder`：每个列同时编码完整 free/paid seat 快照，未选择保持 `0xFF`。
- EPLP/GiveUp：只取消并结束发起参与者；其余成员保留同一 run 并继续翻牌/离场。
- Release build：`dotnet build Server/86JP.Server.slnx -c Release --no-restore`，0 warnings / 0 errors。
- 完整 Application Tests（真实 `PVF_ARCHIVE_PATH`）：1488 passed / 0 failed。
- 完整 Protocol Tests（真实 `PVF_ARCHIVE_PATH`）：924 passed / 0 failed。
- 其余项目最近一次顺序全量：Launcher 63、Pvf 60、Host 17、Infrastructure 314 passed / 2 intentional skipped、LauncherApi 50、Networking 65、Persistence 353。
- 合计：3334 passed / 2 intentional skipped / 0 confirmed failures；`git diff --check` passed。

一次顺序全量执行中，既有 `NpcShopPurchaseTests.Mixed_payment_and_split_delivery_are_one_committed_purchase` 在 SQLite schema 初始化处偶发 `ObjectDisposedException`；该测试单独复跑 1/1、Application 全量立即复跑 1488/1488，堆栈未经过本次地下城代码。证据将其保留为既有并行/连接池偶发项，不将其伪报为本次功能失败或已修复。
