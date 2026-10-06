---
kind: rks-verification
decision: pass
---

# 完成核验：统一物品容器与共享材料

核验日期：2026-10-03。目标是 TaskContract revision 1 的代码、迁移实现和验证交付，不包含部署或新增 A21 实机验收。完整结果通过 [独立评审](../reviews/change-review.md)。基线为 `fd14e7172b0b6a1b39d194b342e4421d470e69bf`，分支 `task/warehouse-storage`。

## 当前证据

最终测试在同一工作树顺序运行，没有并行写共享 .NET 输出。真实 PVF 为 `E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf`，SHA256 `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`。

通用复现命令：设置 `PVF_ARCHIVE_PATH` 后执行 `dotnet test Server/tests/Dfo.Server.<项目>.Tests/Dfo.Server.<项目>.Tests.csproj --no-restore --logger "trx;LogFileName=<记录名>.trx" --results-directory artifacts/unify-item-containers`。以下记录名的同名 `.log` 保存原始输出；路径相对工作树根。

| 项目 | 通过 | 失败 | 跳过 | 最终 TRX |
|---|---:|---:|---:|---|
| Application | 2805 | 0 | 0 | `artifacts/unify-item-containers/Application.trx` |
| Persistence | 406 | 0 | 0 | `artifacts/unify-item-containers/Persistence-final.trx` |
| Protocol | 1246 | 0 | 0 | `artifacts/unify-item-containers/Protocol.trx` |
| Infrastructure，有 PVF | 766 | 0 | 2 | `artifacts/unify-item-containers/Infrastructure.trx` |
| Networking | 86 | 0 | 0 | `artifacts/unify-item-containers/Networking.trx` |
| Host | 118 | 0 | 0 | `artifacts/unify-item-containers/Host-final.trx` |
| Infrastructure，缺 PVF 场景补跑 | 2 | 0 | 0 | `artifacts/unify-item-containers/Infrastructure-no-archive.trx` |

两个跳过是“归档不存在”条件与真实 PVF 环境互斥；移除当前测试进程的 `PVF_ARCHIVE_PATH` 后，以 `--no-build --filter FullyQualifiedName~A_missing_archive` 补跑通过。按独立用例计数：2805+406+1246+768+86+118=5429，均取得通过证据，不重复累计真实 PVF 两项矩阵或历史专项测试。

首次 Persistence 2 失败、Host 3 失败及修正性质保存在 [TASK-004](../tasks/TASK-004-verification-and-docs.md) 与原始 `Persistence.trx`、`Host.trx`；最终完整重跑通过。没有用跳过或降低资产断言掩盖失败。

## 成功条件映射

| 条件 | 当前决定性证据 |
|---|---|
| SC-1：统一身份及隔离 | `ContainerIdTests`、`SharedMaterialContainerTests`、`SqliteItemDomainStoreTests`：同账号双角色共享、异账号独立、账号金库同索引不冲突、无角色在线绑定也可按账号访问。 |
| SC-2：材料统一读写且关闭旧源 | Grant/整笔 Add、技能材料、分解、强化、丢弃、配方、开包、商店、任务/教程与邮件测试纳入完整项目回归；GM 实际 PVF 官方邮件发放和领取读取真实物品槽，同时断言旧余额不变。旧接口/状态/结果分叉生产引用为0。 |
| SC-3：金库存取守恒及正常拒绝 | `SharedMaterialPvfTests` 用真实 PVF 对全部11种材料与两类金库执行拆分、合并及取回；`SharedMaterialContainerTests`、`WarehouseMoveSynchronizationTests`、`WarehouseStorageTests` 的领域/协议测试另覆盖固定槽、超量、满仓、旧会话/越权和失败不变性。 |
| SC-4：迁移准确、幂等、原子 | `SharedMaterialMigrationTests` 使用真实临时 SQLite，覆盖11列/多账号、零值、int.MaxValue、负值/越界、既有槽冲突、旧唯一索引、故障回滚和再次启动。迁移代码额外明确拒绝非整数值；旧列只保留历史值。 |
| SC-5：既有容器与身份不回归 | 完整 Application/Persistence/Protocol/Host 回归覆盖背包、装备、时装、宠物、UID与附加属性、入场重载及真实TCP宿主测试；架构检查继续扫描全部五个相关程序集。 |
| SC-6：结果和通知依赖当前容器 | 统一 effect/消费/邮件/奖励路径及其回滚测试通过；replay 非零共享槽不会在快照后误清空；材料耗尽不发正 itemId/count=0。独立完整结果评审批准，未发现另一套材料结算。 |
| SC-7：说明和集中实机步骤 | [现行模型与迁移](../../../AccountMaterialBag.md)、[共享材料实机清单](../../../SharedMaterialHumanTest-20261003.md)、相关 protocol 实现记录与 catalog 已更新；保留既有金库和教程反馈，新材料客户端行为不标 ClientVerified。 |

## 最终一致性检查

- 初次评审与控制器核对 `artifacts/unify-item-containers/review-source-manifest.json` 的143个变更源/测试路径全部一致。首次暂存时检查到9个新增源/测试文件的 CRLF 或多余文件尾空行，随后仅统一换行与文件尾；`eol-normalization.json` 可从最终文本精确重建原哈希，`final-source-manifest.json` 记录当前143个路径。最终回归后没有语义改动，既有通过结果覆盖当前内容，不为格式整理重复运行整套测试。
- `source-audit.json` 记录旧类型搜索无结果，旧11列的生产引用只在 `CharacterDatabaseSchema.cs` 和 `SharedMaterialMigration.cs`。这是当前源代码审计，不是运行中数据库迁移证明。
- `git diff --cached --check` 通过，包含全部新文件，目标仍为已隔离工作树与原基线。提交后另核对父提交、分支、主干未变化和工作树干净，提交哈希在交付消息报告，不回写产生第二次提交。
- 本次未部署、未迁移线上库或重启现有运行服务；测试 Host 使用隔离临时环境。新客户端验收明确属于后续人工操作，不是本契约代码交付的缺失前置条件。
