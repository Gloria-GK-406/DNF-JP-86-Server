# Rebase 1：变基到主干 dbeba6aa7（表结构顺延到 v10）

用户在对话中授权"先变基再测"。本记录说明变基过程、冲突处理、版本顺延和验证结果。

## 提交与备份

| 项 | 值 |
|---|---|
| 变基前 HEAD | `6b5935890`（基线 `20bcaf37c` 之上 14 个本任务提交，含 planning） |
| 本地备份分支 | `backup/player-portal-329-pre-rebase` → `6b5935890`（只在本地，未 push） |
| 目标 | `origin/main` = `dbeba6aa7`（基线之后 29 个提交：拍卖行 #324、金币寄售 #322、拍卖券、个人商店 #43/#335、光环幻化栏 #327） |
| 变基后、本记录提交前 HEAD | `b76d6f4cd` |
| 本记录所在提交 | `player-self-service-portal: rebase onto main（schema 顺延到 v10）` |

13 个提交全部重放成功，没有执行 `--abort`。只有 `c23bbb92d`（TASK-01）冲突，其余提交自动应用。TASK-01 的提交标题改为"表结构 v10（变基前为 v7）与玩家后台持久化"，其它提交信息不变。

## 冲突与处理（都在变基后的 TASK-01 提交 `bb0cf5740` 里）

两边都改过的文件只有 6 个，其中 4 个冲突，2 个自动合并。

| 文件 | 处理 |
|---|---|
| `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 主干 `Version = 9`，本分支 `7` → 取 **`10`**。表声明自动合并：主干的拍卖行 3 表、`private_store_custody`、`characters.aura_skin_slot` 与本分支的 3 张玩家后台表、4 个索引、`gm_audit` 两列都保留。 |
| `Tool/DbMigrate/Migrator.cs` | 保留主干 `[6] AuctionTablesUpgrade`、`[7] PrivateStoreCustodyUpgrade`、`[8] AuraSkinSlotUpgrade`，本分支步骤改登记为 **`[9] = PlayerPortalUpgrade.Apply`**。 |
| `Document/DatabaseSchemaMigration.md` | 保留主干"6 → 7、7 → 8、8 → 9"三条；本分支条目改为 **"版本 9 → 10（`PlayerPortalUpgrade.cs`）"**，放在主干条目之后；末句"只认表结构版本 6 的服务端"改为"版本 9"。 |
| `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs` | 表计数改为 **123**。算法：基线 116；主干新增 4 张表（`auction_listings`、`auction_custody`、`auction_trades`、`private_store_custody`）→ 120，`aura_skin_slot` 是列，不增加行；本分支新增 3 张表（`player_portal_settings`、`player_portal_sessions`、`player_portal_operations`）→ 119，`gm_audit` 的两列不增加行。合计 116 + 4 + 3 = 123。两边的 `Assert.Contains`（拍卖行两表、玩家后台三表、`gm_audit` 新列形状）都保留。测试通过，证实计数正确。 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 自动合并。主干的 `AddAuction`、`AddPrivateStore` 与本分支的 `AddPlayerPortal` 都在。 |
| `Server/src/Dfo.Server.Persistence/PersistenceModule.cs` | 自动合并。主干的拍卖行、个人商店存储与本分支的 `SqlitePlayerPortalPersistence` 都在。 |

同一提交里一并修改了以下版本号，保证 TASK-01 提交本身前后一致：

- `Tool/DbMigrate/PlayerPortalUpgrade.cs`：类注释改为"schema 9 to 10"，"复制自 version-7 声明"改为"version-10"。SQL 语句不变：玩家后台的三表、四索引、两列与主干的新表没有交叉。
- `Tool/DbMigrate.Tests/PlayerPortalMigrationTests.cs`：旧库基线由 v6 改为**主干的 v9 结构**。做法不变：新建当前形状的库，删去本分支的 3 张表和 `gm_audit` 两列，就得到主干 v9 的形状，再设 `user_version=9`。断言改为 `From = 9`、`Version == 10`；用例名 `Version_six_*` 改为 `Version_nine_*`。为了覆盖主干 v7–v9 的数据，基线新增一条 `auction_trades` 行，`characters` 写入 `aura_skin_slot`（71 号角色为 1）；`auction_trades` 加入逐行比对的保留表，`characters` 本来就整行比对。断言"迁移后形状与新建库一致"保持不变，并且通过。

## 变基后的额外修正（本记录所在提交）

- `Document/tasks/player-self-service-portal/用户测试流程.md` 的"测试库"一行：原文写"迁移到表结构 v7"，现注明该库**已作废**。原因：这份库是按变基前的编号迁移的，`user_version=7`，但没有拍卖行表；主干的 v7 恰好表示"已有拍卖行表"。v10 的 DbMigrate 遇到它会从 `[7]` 开始执行，跳过 `[6] AuctionTablesUpgrade`，结果库里缺拍卖行表。需要从原库重新复制，再用变基后的 DbMigrate 迁移到 v10。

## 语义交叉检查

先列出主干在基线之后改过的全部非协议文件，再逐项核对：

| 检查项 | 结论 |
|---|---|
| 审计动作名集合、`GmAuditFilter`、`gm_audit` 写入 | 主干没有改 `Dfo.Server.Application/Administration`、`Dfo.Server.Data/Administration`、`Dfo.Server.GmWeb`，拍卖行、个人商店、光环栏也不写 `gm_audit`。无交叉。 |
| `HttpRouteOperation` 枚举顺序、`GmRouteReplyMapper` 范围判断 | 主干没有改 `Dfo.Server.HttpApi`，枚举只有本分支追加的成员，范围判断不受影响。 |
| `GmGameplayRouteTests` 路由计数 | 主干没有新增 GM 路由，该文件只有本分支改过，计数不用调整。 |
| 前端 `AdminApp` 导航、`api.ts` | 主干没有改 `Web/gm-admin`，无交叉。 |
| 邮件 | 主干新增系统邮件资产 `MailAsset.TokenCera`（代币券凭证，`asset_kind='token_cera'`），`MailBuilder` 规定它只能用于非角色来源。本分支的 `OfficialMailDelivery` 只构造装备、堆叠物和金币，不生成 `TokenCera`；`prepared` 的判断 `asset is MailAsset.Gold ? …` 遇不到 `TokenCera`。build 零警告，说明没有不完备的 switch。无冲突。 |
| `PremiumServiceType` 新成员（8、29） | 本分支不用这个枚举，无交叉。 |
| `characters.aura_skin_slot` | 本分支读写 `characters` 时都显式列出列名，新列有缺省值，无影响。 |

**一个观察（不阻塞）**：玩家可以从邮件物品目录里选择任意物品；目录是否会列出代币券凭证物品 2681762，本次没有核实。即使列出来，它也只会作为普通堆叠物发送和领取，不会给账号加代币券，不构成发券漏洞。按主干的设计，客户端会从这一行的金币字段读出面额，此时读到的是 0。

## 验证（变基后 `b76d6f4cd` 的工作区，含本提交的文档改动；代码与本提交一致）

| 检查 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx` | 0 警告 0 错误 |
| `Tool/DbMigrate.Tests` | 31/31 通过（含 `Version_nine_upgrade_adds_the_player_portal_closed_and_keeps_everything_else`、`Version_nine_step_leaves_...`，以及主干的 v6/v8 用例和 `A_migrated_database_has_exactly_the_shape_the_server_builds`） |
| `Web/gm-admin`：`npm ci && npm test && npm run build` | 16 个文件 195/195 通过；构建成功（只有 Vite 的 chunk 大小提示，变基前已存在） |
| 全量 `dotnet test Server/86JP.Server.slnx`（`PVF_ARCHIVE_PATH` 指向 `DNFClient-A21-protocol\Script.pvf`，只读；开始于北京时间 04:34） | 首轮 11 个失败、3 个跳过，逐一归因见下 |

首轮失败归因：

1. **Protocol.Tests 4 个与 Networking.Tests 3 个**（`CommandRegistrationTests.*`、`ChannelControlMessagesIntegrationTests.*`），报错 `The database is at schema version 7 and this server needs 10`。原因：两个测试项目 `bin/Debug/net10.0/Data/dfo-server.db` 里残留着变基前测试运行（03:06）生成的库，`user_version=7`，按旧编号含玩家后台表，0 个账号。这是测试副产物，不是代码问题。已把这两个文件挪到 scratchpad（没有删除），重跑后 Protocol.Tests 2334/2334、Networking.Tests 222/222 全部通过。注意：其它用过变基前构建的目录也会遇到同样的问题。
2. **`MirrorDungeonEntryTests.Refused_mirror_entry_..._reserve_daily_entry(boundary: "quota")`**：已知问题，北京时间 00:00–06:00 会失败；本轮在 04:3x 运行。与本分支无关。
3. **`ComponentLifecycleTests.ContentPreparationBlocksBothListeners...`、`LauncherHttpHostStartupTests.HttpWithoutGame_...`**：全量并行时超时（25 s / 30 s）。单独运行 Host.Tests 时两者都通过，按过滤器单跑也是 26/26 通过。判定为高负载下的偶发失败（首轮期间还在并行编译 DbMigrate.Tests）。
4. **`GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs`**：首轮失败，单独重跑 Host.Tests（322 通过 / 1 失败 / 1 跳过）仍然失败。断言来自 `ContentWarmupTests.AssertWarmedAsync`：实际的预热参与者比 `ContentWarmupTests.Participants` 多出 `auction-item-index`。这个参与者由主干的 `PvfAuctionItemDefinitionSource`（#324）注册，而主干的 `ContentWarmupTests.Participants` 没有收录它。`ContentWarmupTests.cs` 与 `Dfo.Server.Infrastructure` 在本分支和 `origin/main` 之间没有任何差异，所以这是**主干自身的缺陷**：真实 PVF 下主干同样会失败。本分支没有修，留给主干处理。修法是在 `Participants` 里加上 `"auction-item-index"`。

跳过的用例：`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`、`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`（`Skip.If(ContentArchive.Available)`，可以读到 PVF 时按设计跳过）；`HostShutdownOrderTests.Sigterm_...`（`[NonWindowsFact]`，Windows 上跳过）。都是环境决定的跳过。

其余项目首轮即通过：HttpApi 146、DbMigrate 31、Pvf 82、Infrastructure 1051（2 跳过）、Persistence 478（含 `SchemaOwnershipTests`、`SqlitePlayerPortalPersistenceTests`）、Application 6863/6864（唯一失败是 MirrorDungeon）。

## 对 TaskContract、Spec、Plan 等记录的影响

- Spec、Plan、TASK-01、TASK-07、各轮 review 与 `verification/completion-evidence.md` 中的"表结构 v7""步骤 `[6]`""版本 6 → 7""`user_version 7`"都是变基前的编号，现在应理解为 **v10、步骤 `[9]`、版本 9 → 10**。这些是历史记录，本次不改写。`completion-evidence.md`"合并前"一条写的"顺延为版本 8、步骤 `[7]`"已经过时，以本记录为准（主干在此期间又合入了 v8、v9）。
- TaskContract 与 Spec 的要求本身不变：三张表、两列、玩家后台默认关闭、迁移保留既有数据。改变的只是版本号。
- 浏览器实测截图与 TASK-07 的证据（`artifacts/player-portal-329/` 下的 Release 产物与测试库）都基于变基前的提交。用户测试服需要重新 publish，并**按上文重建测试库**；原测试库与原 host 产物不能直接用于变基后的代码。

## 未解决

- 主干的 `ContentWarmupTests.Participants` 缺少 `auction-item-index`（见验证第 4 条），需要在主干修复。
- 用户测试环境（host 产物与测试库）需要主控按变基后的提交重建。

## 测试环境重建

基于提交 `dda6fe28a`（工作区无其它未提交改动），执行于 2026-11-14 04:52–04:56（北京时间）。材料都在 `artifacts/player-portal-329/`（被 `.gitignore` 忽略），日志为其 `logs/rebuild-*.log`。没有 commit，没有使用 stash / checkout / restore，没有进入或改动主干工作区，没有留下常驻进程。

### 旧材料

- 两份旧库副本整目录移到 `db/stale-pre-rebase/agent`、`db/stale-pre-rebase/user`，未删除。
- 旧发布目录移到 `host-stale-pre-rebase/`，未删除；新发布写入空的 `host/`，避免旧的 `wwwroot/gm/assets` 残留。

### 原库未被改动

原库 `E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db`。操作前（`rebuild-src-before.log`）与全部操作后（`rebuild-src-after.log`）：

| 文件 | 大小 | 修改时间 | SHA-256 | 前后 |
|---|---|---|---|---|
| 主文件 | 20262912 | 2026-10-30 09:12:31 | `89b2915aedc24e54638c3bddd73ff8c952015d36497f7cadf2a62de0834ffb39` | 一致 |
| `-wal` | 4140632 | 2026-10-30 10:54:06 | `fd8e232cad60efb636320602c5540936c0bb5c72306a465ef96c195e75c1f6ad` | 一致 |
| `-shm` | 32768 | 03:07:50 → 04:52:18 | `c8e2547f…`（内容不变） | 只有修改时间变化：只读连接取读锁时会更新 WAL 索引的时间，不是数据 |

- 开始前主干服务未运行：7001、18080、18081 无监听，没有任何进程加载 `Dfo.Server.Host.dll`。

### 两份副本

- **取副本**：`tools/copy_db.py` 以 `mode=ro` 打开原库，用 backup API 分别独立写出 `db/agent/dfo-server.db` 与 `db/user/dfo-server.db`。两份迁移前都是 `user_version 0`、integrity ok、账号 4、角色 9（未删除 8）。
- **迁移**：先 `dotnet build Tool/DbMigrate -c Release`（0 警告 0 错误），再对两份副本各运行 `dotnet run --no-build --project Tool/DbMigrate -c Release -- <副本>`，都输出 `Migrated from schema version 0 to 10.`，退出码 0。
- **迁移备份**：
  - agent：`artifacts/player-portal-329/db/agent/dfo-server.db.v0-20261113205229.bak`
  - user：`artifacts/player-portal-329/db/user/dfo-server.db.v0-20261113205230.bak`
  - 两份备份 SHA-256 都是 `fb729ace…`，与变基前那次相同，说明原库内容没变。
- **迁移后核对**（`tools/inspect_db.py`，`immutable=1` 只读）：两份相同。
  - `user_version 10`，integrity ok，表 123 张；
  - 有 `auction_custody`、`auction_listings`、`auction_trades`、`private_store_custody`，即重建前缺的主干表都在；
  - 账号 4、角色 9（未删除 8），与原库一致；
  - `player_portal_settings`、`player_portal_sessions`、`player_portal_operations` 都是 0 行；
  - `gm_audit` 83 条，全部为 `gm`；管理员 1 个。
  - 迁移后两份主文件的 SHA-256 相同，都是 `fd0227e1…`，说明迁移是确定的。

### user 副本封存

- 路径：`artifacts/player-portal-329/db/user/dfo-server.db`。
- 迁移后主文件 SHA-256：`fd0227e16f27e481c046db0f3f6fa4c3cef0298c301d09f23b55a642f16de394`。全部操作结束时复核，未变（`rebuild-user-hash-final.log`）。
- 日志模式为 `delete`，目录里只有主文件和迁移备份，没有 `-wal` / `-shm`。
- 没有用 Host 打开过。核对只用 `immutable=1` 只读打开：版本 10，计数如上。

### agent 副本准备

只改 `db/agent/dfo-server.db`，脚本 `tools/prepare_agent.py`（只认 `db/agent` 路径，不打印凭据）：

- **管理员**：原管理员整行存入旁表 `agent_saved_gm_administrators`，再清空 `gm_administrators` 与 `gm_sessions`（23 条旧会话）。Bootstrap 后新管理员 ID 2，权限 15。
- **测试玩家**：
  - 第一次冒烟时经 gate `/register` 用 `agent-credentials.env` 里现有的登录名与密码注册，返回 201，新账号 ID 6（无角色）。
  - 停服后把它的密码记录复制到账号 4（4 个未删除角色、1 个已删除），原值存入旁表 `agent_saved_account_passwords`。
- `agent-credentials.env` 的取值没有变化：账号 ID 仍是 4，其登录名与账号 4 的 `m_id` 一致（脚本只比对、不输出）。
- 现状：v10，integrity ok，日志模式 `wal`（Host 打开后切换），表 125 张（123 + 两张旁表）；账号 5、角色 9（未删除 8）；玩家后台三表 0 行；`gm_audit` 84 条，全部为 `gm`（多出 Bootstrap 管理员的一次登录）。

### 发布

- `cd Web/gm-admin && npm run build`：退出码 0，只有既有的 chunk > 500 kB 提示；入口 `assets/index-BsCROQG6.js`、`assets/index-C3lW4IzP.css`。
- `dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release -o artifacts/player-portal-329/host`：退出码 0（`rebuild-publish.log`）。
- `diff -r Web/gm-admin/dist host/wwwroot/gm` 无差异；`index.html` 两边 SHA-256 都是 `9b5d023f…`。
- `host/appsettings.json` 中 GmWeb 仍为 `Enabled: false`，由脚本用环境变量打开。
- 两个启动脚本只把注释里的"migrated to v7"改成"v10"，`bash -n` 通过，其余不变。

### agent 冒烟

启动前确认 43xxx 无监听。两次各几十秒，日志无 `fail` / `crit` / 未处理异常（`rebuild-agent-host.log`、`rebuild-agent-host-2.log`）。

- 第一次（PID 49656，04:54:19 启动，`rebuild-smoke-1.log`）：
  - 6 个 TCP 端口（43001、43010、43011、43100、43080、43081）都由该 PID 监听；
  - `http://127.0.0.1:43081/gm/` 与 `http://localhost:43081/gm/` 均 200，正文含入口脚本名，入口 JS 200；
  - `/api/player/v1/auth/status` 200 `{"enabled":false}`；`/api/gm/v1/auth/status` 200 `{"initialized":true}`；
  - Bootstrap 管理员经 csrf → 登录 200，`auth/session` 200；
  - gate `/register` 201。
- 第二次（PID 49904，04:55:28 启动，`rebuild-smoke-2.log`）：账号 4 与注册账号经 gate `/login` 都返回 200。
- 结束：每次都核对可执行文件为 `C:\Program Files\dotnet\dotnet.exe`、命令行为 `dotnet Dfo.Server.Host.dll`、已加载模块为 `artifacts\player-portal-329\host\Dfo.Server.Host.dll`、43081 监听者是该 PID、启动时间与本次启动相符，全部一致后只对该 PID `Stop-Process`。之后 43xxx 无监听。后台任务报告的退出码 127 是进程被结束的结果。

### 其它

- 结束时 UDP 12311/12312 无人占用，原先挡住用户测试服的占用已经不在；是否就绪由主控判断并填写。
- `Document/tasks/player-self-service-portal/用户测试流程.md` 已更新：提交 `dda6fe28a`、发布时间、测试库为 v10、迁移备份路径、完整 SHA-256、玩家后台三表 0 行。"就绪情况"一行未改，留给主控。文档 CR 为 0。
