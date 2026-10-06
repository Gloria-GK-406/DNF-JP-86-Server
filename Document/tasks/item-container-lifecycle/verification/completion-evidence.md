---
kind: rks-verification
decision: pass
---

# 容器物品生命周期完成证据

核验日期：2026-10-03。目标为 `task/warehouse-storage` 上已接受基线 `32039260ded8d251c5052dfcec597213c561668a` 之后的完整最终差异。独立完整审查见 [change-review.md](../reviews/change-review.md)，结论 `approved`，无阻断项。各 implementation/migration report 是阶段交接记录；当前完成状态以本文件及 [spec-result.md](../spec-result.md) 为准。

## 成功条件

| 条件 | 最终结果与决定性证据 |
|---|---|
| SC-1 回退及语义分离 | 旧尝试已恢复至上述基线，回退证据见 [旧方案结果](../../unified-item-record/spec-result.md)。`IItemContainer` / `IItemDomainStoreSession` 明确分开 Get、Take、Put、Update；`ItemRecord` 是 detached 能力，`ItemReference` 是当前占用引用，两者共享唯一完整属性值 `ItemSnapshot`。 |
| SC-2 转移及拒收 | `Persistence.Tests/Inventory/Lifecycle/StorageLifecycleTests.cs` 验证 Take 留物品行、Put 保留 UID/完整字段、拒收不消耗记录、重复及伪造接收拒绝、交换与跨后端材料回滚。既有仓库、换装、整理与拆合回归同时通过。 |
| SC-3 引用更新及失效 | StorageLifecycle、CreatureLifecycle、Application EquipmentInstance/CreatureTransformation/Runtime 测试验证 Get 不移除、Update 使用最新状态、取出后同 UID 回槽仍使旧引用失效、跨事务拒绝，以及装备/宠物交替修改不覆盖其他字段。 |
| SC-4 显式销毁及事务完整性 | 真实 SQLite 验证 Take 与 DeleteItem 的行生命周期不同、未安置 record 在中途 Flush 后仍不能提交、重复销毁拒绝、数量守恒和异常整体回滚。MailCustody 验证附件托管、领取、拒绝与重试；GM 邮件 Host 集成通过。 |
| SC-5 统一模型及旧入口退出 | 对全部 `Server/src/**/*.cs` 审计，StoredItem、ContainerSlotItem、InventoryStorageItem、InventorySlotContent、InventoryItemCreationFacts、ItemTransferSource、PutTransferred、PutNew 及旧专用创建/装备转移服务引用均为零。原始结果在 `artifacts/item-container-lifecycle/production-model-audit.log`。Core 保留于数据库、协议及完成回执投影，PVF 内部分类不作为额外物品模型。完整 82 字节、时装/宠物细节和数组隔离由真实存储往返测试覆盖。 |
| SC-6 兼容回归 | 解决方案构建 0 警告/0 错误；下列八项目全部退出 0。协议字节期望未改写。历史载入 characterization 的 SQL/core 种子保持不变，实际 slot.kind 决定 UID 的预期变化明确记录于 [持久化迁移报告](../persistence-test-migration-report.md)。 |

## 最终构建与测试

构建命令：`dotnet build Server/86JP.Server.slnx --no-restore -v quiet`，退出 0；日志 `artifacts/item-container-lifecycle/solution-final-build.log`。

测试前设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。构建后依次对下列项目执行：

```text
dotnet test Server/tests/<project>/<project>.csproj --no-build --no-restore -v quiet
  --logger "trx;LogFileName=<project>-final.trx"
  --results-directory artifacts/item-container-lifecycle/test-results
```

| 项目 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| Dfo.Server.Persistence.Tests | 458 | 0 | 0 |
| Dfo.Server.Application.Tests | 2837 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 1247 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 768 | 0 | 2 |
| Dfo.Server.Host.Tests | 118 | 0 | 0 |
| Dfo.Server.HttpApi.Tests | 50 | 0 | 0 |
| Dfo.Server.Networking.Tests | 86 | 0 | 0 |
| Dfo.Pvf.Tests | 80 | 0 | 0 |
| 合计 | **5644** | **0** | **2** |

逐项目退出码在 `artifacts/item-container-lifecycle/solution-final-tests.json`，计数由该文件指定的八份最终 TRX 单独提取至 `final-test-counts.json`，未重复计入阶段测试。每项原始输出为同目录 `<project>-final.log`，TRX 为 `test-results/<project>-final.trx`。

两个跳过项是 Infrastructure 的 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog` 和 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`。原因均为 `An archive is reachable from this working tree.`：它们验证缺失 PVF 的环境，与本轮真实 PVF 可达条件互斥，不是被禁用的失败用例。

## 实现期间发现并修复

- 补齐普通奖励空槽创建的 lifecycle 注入；宠物拾取保留实际分配身份；装备初次查找跳过无存储空间，零耐久修改不做无效写入。
- 保留已有关闭槽的更新限制、共享材料增长限制，以及每日 Refill 原有补量语义。相关行为先观察失败再修复，见 `container-update-guard-red.log`、`creature-pickup-red.log` 及最终 Application/Protocol 回归。
- 真实损坏 SQLite 数据证明时装颜色越界会错误抛出 OverflowException；库存和邮件共用 Int64 范围检查，恢复 InvalidDataException，并覆盖负值、65536、long.MaxValue 与合法边界。RED/GREEN 和六项邮件回滚样本见持久化迁移报告及其日志。

提交前对 25 个新增或识别为重命名的文本文件统一行尾并清除尾空白；逐文件非空白内容相同，独立审查确认 C# 逻辑及 SQL/测试 DDL 语义不变，路径见 `artifacts/item-container-lifecycle/final-whitespace-paths.txt`。随后重新构建仍为 0 警告/0 错误，Persistence 再次 458/458 通过（`persistence-after-format.log` / `test-results/persistence-after-format.trx`），不把重复回归计入总数。

`git diff --check` 与完整 `git diff --cached --check` 均无输出。提交前 HEAD 仍为接受基线，主检出干净，`main` 仍为 `6ee658cdf14208d819360c40b3b47c4717ae3499`。

## 证据边界

以上是 CurrentImplementation、真实 SQLite、真实 PVF 与本地集成测试证据；没有执行 A21 实机操作，不新增 ClientVerified 结论。数据库 schema、玩法及报文布局没有作为本任务改动目标；未部署或操作运行数据库。独立物品跨独立存储后端仍不支持，跨库继续受既有单可失败提交参与者约束。
