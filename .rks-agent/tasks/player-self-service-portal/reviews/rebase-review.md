---
kind: rks-review
verdict: approved
---

# 变基独立审查：player-self-service-portal → origin/main dbeba6aa7

## 审查对象与范围

| 项 | 值 |
|---|---|
| 变基前 | `backup/player-portal-329-pre-rebase` = `6b5935890`，基线 `20bcaf37c` |
| 变基后 | `HEAD` = `dda6fe28a`，`merge-base(HEAD, origin/main)` = `dbeba6aa7`（= `origin/main`） |
| 被审记录 | `reviews/rebase-1.md` |
| 适用准则 | reviewing-work / Code（完整代码变更，限于"变基是否保持功能等价、冲突解决是否正确、与主干有无语义交叉"） |
| 审查方式 | 独立审查者，只读；未改动工作区文件（只写本文件），未 commit，未启停进程 |

不在范围内：本任务功能本身的正确性（已由前几轮审查覆盖），`artifacts/player-portal-329/` 与 `用户测试流程.md`（另一 worker 在改）。

## 1. 变基前后功能等价 —— 成立

- `git range-diff 20bcaf37c..backup/player-portal-329-pre-rebase origin/main..HEAD`：13 个提交一一对应，12 个为 `=`（补丁完全相同），只有 TASK-01（`c23bbb92d` → `bb0cf5740`）为 `!`；第 14 个 `dda6fe28a` 是新增的变基记录提交（只含 `rebase-1.md` 和测试流程文档一行）。
- 另把 `git diff 20bcaf37c backup/...` 与 `git diff origin/main b76d6f4cd` 去掉 `index`/`@@` 行后逐行比对，差异只有：
  - `CharacterDatabaseSchema.Version`：`6→7` 变为 `9→10`；
  - `Migrator.Steps`：`[6]` 变为 `[9]`（上下文由主干的 `[6]–[8]` 取代 `[3]–[5]`）；
  - `SchemaOwnershipTests` 表计数：`116→119` 变为 `120→123`；
  - `DatabaseSchemaMigration.md` 条目编号与"只认版本 9"；
  - `PlayerPortalUpgrade.cs` 两处注释的版本号；
  - `PlayerPortalMigrationTests`：版本号、用例名，以及新增的 `aura_skin_slot` 列值与一条 `auction_trades` 行（覆盖主干 v7–v9 数据）。
- 两边改动文件清单相同（变基后多一个 `rebase-1.md`）。玩家后台的服务、端点、前端、持久化 SQL 全部逐字节不变。

结论：唯一的实质差异就是结构版本顺延（v7→v10、`[6]`→`[9]`）和测试计数/基线，与 worker 所述一致。

## 2. 冲突解决 —— 正确

- **`CharacterDatabaseSchema`**：`git diff origin/main HEAD` 只有 `Version 9→10`、`gm_audit` 末尾两列、3 张玩家后台表、4 个索引；主干的拍卖行 3 表、`private_store_custody`、`characters.aura_skin_slot` 均未被触碰，完整保留。
- **`Migrator`**：相对主干只加一行 `[9] = PlayerPortalUpgrade.Apply`，主干 `[6] AuctionTablesUpgrade`、`[7] PrivateStoreCustodyUpgrade`、`[8] AuraSkinSlotUpgrade` 原样保留。
- **`PlayerPortalUpgrade` 幂等性**：建表、建索引都是 `IF NOT EXISTS`；两列经 `Shape.HasColumn` 判断后才 `ALTER`；整步在一个事务里。经 legacy 步（`[0]`，按当前声明建缺失表）进来的库已有这些表和列，到 `[9]` 时不再变化。用例 `Version_nine_step_leaves_...` 对同一库连续执行两次 `Apply` 并断言形状不变；`Version_nine_upgrade_...` 在迁移后再次 `Migrate` 断言 `From==To`、无备份、形状与数据不变。
- **迁移结果与新建库一致**：`Version_nine_upgrade_...` 断言 `ShapeOf(fresh) == ShapeOf(migrated)`（列类型/非空/缺省/主键 + 全部索引与触发器的 SQL）；主干的 `A_migrated_database_has_exactly_the_shape_the_server_builds`（无版本旧库走完 `[0]..[9]` 全链）同样通过。
- **v9 基线是否真实反映主干**：基线 = 当前声明建库后删去本分支增加的 3 表（其索引随表删除）与 `gm_audit` 两列。由于本分支对声明的改动恰好只有这些（第 1 节 diff 已证），得到的形状就是 `origin/main` 的 v9 形状；`gm_audit` 两列在末尾，`DROP COLUMN` 后列序与主干一致。基线中写入了 `aura_skin_slot=1` 与 `auction_trades` 行并逐行比对，覆盖了主干新结构上的数据保留。
- **`SchemaOwnershipTests` 计数 123**：主干 120 + 本分支 3 张表 = 123，`gm_audit` 两列不增行；测试通过（worker 报告 Persistence 478 通过，本次未重跑该项目）。
- **`DatabaseSchemaMigration.md`**：主干 6→7、7→8、8→9 三条完整保留，新增 9→10 条目位于其后，"只认表结构版本 9"正确；文档其它处无硬编码版本号需要随改。
- `ApplicationModule`、`PersistenceModule` 自动合并结果：相对主干各只多一行（`AddPlayerPortal()`、`SqlitePlayerPortalPersistence` 注册），主干注册均在。

## 3. 与主干新增功能的语义交叉 —— 复核同意"无交叉"

主干在基线后改动的非协议文件清单（`git diff --name-only 20bcaf37c origin/main`）中：

- 不含 `Dfo.Server.HttpApi`、`Dfo.Server.GmWeb`、`Dfo.Server.Application/Administration`、`Dfo.Server.Data/Administration`、`Web/gm-admin`。因此 `HttpRouteOperation` 的成员顺序与 `GmRouteReplyMapper` 范围判断、审计动作集合与 `gm_audit` 写入、GM 路由计数、前端导航均只有本分支在改，无交叉。
- **邮件**：主干新增 `MailAsset.TokenCera`（`Mail.cs`、`MailboxDomainService`、`SqliteMailboxRepository`）。唯一生产者是 `AuctionHoldings`（金币寄售成交），`MailBuilder.Build` 要求其来源不是角色。本分支 `OfficialMailDelivery` 只调用 `AddEquipment/AddStackable/AddGold`，`prepared` 的三元判断 `asset is MailAsset.Gold ? …` 遇不到 `TokenCera`；`SendPreparedAsync` 对 TokenCera 的分支也不会被本分支触发。无冲突。
- **账户货币**：本分支 `PlayerPortalCeraDomainService` 经 `IAccountWalletDomainService` 加点券，与主干寄售成交入账走同一钱包服务；主干只改了 `IPremiumServiceDomainService`，钱包接口未变。无交叉。
- **`characters.aura_skin_slot`**：本分支持久化 SQL 不读写 `characters`（只 `LEFT JOIN accounts` 取 `m_id`）。无影响。
- **启动预热**：本分支未注册任何 `IContentWarmup`，未改 `InfrastructureModule`（见第 4 节）。

## 4. `GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs` —— 确认是主干自身缺陷

- 复跑失败信息：`ContentWarmupTests.AssertWarmedAsync` 第 237 行 `Assert.Equal(Participants…, warmups…)`，实际集合在第 2 位多出 `"auction-item-index"`，其余一致。
- 来源：主干 #324 在 `InfrastructureModule.AddContentWarmups` 加了 `.AddContentWarmup<IAuctionItemDefinitionSource>()`（`PvfAuctionItemDefinitionSource.Name => "auction-item-index"`），并在 `ContentWarmupTests.Holders` 字典里加了 `PvfAuctionItemDefinitionSource` 一项，但**没有**把 `"auction-item-index"` 加进同文件的 `Participants` 列表。
- `git diff origin/main HEAD` 对 `ContentWarmupTests.cs` 与 `Server/src/Dfo.Server.Infrastructure/` 为空；本分支的 `Server/src` 改动中没有任何 `ContentWarmup` 相关代码；该用例方法体本分支未改（本分支只改了同文件另一个用例的路由清单与计数）。所以 `origin/main` 上该用例在有 PVF 时同样失败。
- 该用例是 `[PvfFact]`，无 PVF 时跳过，这解释了主干为何没发现。
- 未在 `origin/main` 实际运行（不建额外 worktree 以保持只读），结论基于上述代码等同性，置信度 High。修法同 worker 所述：`Participants` 加 `"auction-item-index"`，应在主干修。

## 5. 复跑结果（本审查自行执行，HEAD `dda6fe28a`）

| 检查 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx` | 0 警告 0 错误 |
| `dotnet test Tool/DbMigrate.Tests` | 31/31 通过 |
| `dotnet test Server/tests/Dfo.Server.Host.Tests --no-build --filter "FullyQualifiedName~Dfo.Server.Host.Tests.PlayerPortal\|FullyQualifiedName~Dfo.Server.Host.Tests.Gm"`（`PVF_ARCHIVE_PATH` 指向 `DNFClient-A21-protocol\Script.pvf`，只读） | 225 个：224 通过，1 失败 = 上述主干缺陷用例 |

## 发现

无阻塞缺陷。

非阻塞残余风险（供主控知悉，不要求本轮修改）：

1. **按旧编号迁移过的库会被静默迁错**：变基前生成的库 `user_version=7` 含玩家后台表、无拍卖行表；新 DbMigrate 会从 `[7]` 开始，跳过 `[6] AuctionTablesUpgrade`，最终盖上 v10 却缺拍卖行表，服务端能启动、到拍卖行功能才出错。`rebase-1.md` 已注明用户测试库作废、需从原库重建；另外各测试项目 `bin/Debug/net10.0/Data/dfo-server.db` 残留的旧库也属此类（worker 已遇到并挪走两份）。本分支从未发布，影响限于本地测试产物。
2. 主干既有的 `Version_six_upgrade_adds_the_auction_tables_...` 用例以"当前声明减拍卖行表"作为 v6 基线，基线里已带有 v8–v10 的结构；这是主干既有写法，`[9]` 步幂等所以仍通过，不影响本次结论。
3. `rebase-1.md` 已提及：玩家邮件物品目录是否列出代币券凭证物品 2681762 未核实。即便列出，也只按普通堆叠物发放，不会给账号加代币券，不构成发券漏洞，但客户端显示面额为 0 可能令人困惑。
