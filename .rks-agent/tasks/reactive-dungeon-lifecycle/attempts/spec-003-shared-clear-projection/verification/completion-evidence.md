---
kind: rks-completion-evidence
verified_at: 2026-09-16
---

# Completion Evidence

- `DungeonRunDomainServiceTests.Shared_clear_opens_once_with_one_revision_and_current_recipients`：共享实例仅产生一次 `DungeonClearChange`，revision 单调，离场成员不会继续成为 recipient。
- `DungeonClearDomainServiceTests`：Boss 房清空和 settlement 条件共同控制清关；队友触发时使用冻结 leader；普通房和旧 room version 不开启。
- `DungeonClearProjectionDeliveryTests`：两名当前冻结成员各收一组清关帧；离场成员、旧 generation 与 replacement connection 均不收。
- `QuestClearVerticalTests`：真实 CommandBus + SQLite 从背包 14 件、Trigger=1 开始；Boss death 已提交时不清关，最后任务物品事务提交为 15 件并将 Trigger 持久化为 0，随后唯一 `DungeonClearChange` 向两名当前成员各投影一次。
- `GetItemHandlerTests.Pickup_send_failure_cannot_erase_the_committed_shared_clear_fact` 与 `MonsterDeathHandlerTests.Death_send_failure_cannot_erase_the_committed_shared_clear_fact`：网络发送抛异常时，权威 `ClearOpen` 已提交且不能重复开启。
- MonsterDeath / GroundPickup / clear projection 定向 Protocol Tests：23 passed / 0 failed。
- Application Tests（设置真实 `PVF_ARCHIVE_PATH`）：1483 passed / 0 failed。
- Protocol Tests（设置真实 `PVF_ARCHIVE_PATH`）：915 passed / 0 failed。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore`：0 warnings / 0 errors。
- `git diff --check`：passed。

第一次未设置 `PVF_ARCHIVE_PATH` 的 Protocol 全量执行有 43 项因找不到 `Script.pvf` 失败；设置与运行环境一致的真实 A21 PVF 后 912 项全部通过，属于测试环境缺失而非代码失败。
