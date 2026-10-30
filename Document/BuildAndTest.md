# 构建、运行与测试排障

构建或排查测试失败前阅读。CI 门、PR 审核合并、完整发布验证及版本规则统一见
[server-ci-release.md](server-ci-release.md)。

## 构建与运行

SDK 选择以 [global.json](../global.json) 为准：`10.0.100`、`rollForward: latestMajor`，
不锁小版本；Nullable、TreatWarningsAsErrors 和 Deterministic 由
[Directory.Build.props](../Server/Directory.Build.props) 开启。

在仓库根目录构建：

```bash
dotnet build Server/86JP.Server.slnx
```

Host 的 content root 是可执行程序目录。开发构建后可在输出目录运行：

```bash
cd Server/src/Dfo.Server.Host/bin/Debug/net10.0
Networking__Enabled=true dotnet Dfo.Server.Host.dll
```

上述环境变量写法适用于 POSIX shell。Windows 使用 PowerShell 的 `$env:Networking__Enabled = 'true'`，
然后运行 `dotnet Dfo.Server.Host.dll` 或 `Dfo.Server.Host.exe`。
默认网络、Launcher HTTP 与 GM 开关均关闭；端口与配置默认值直接查
[appsettings.json](../Server/src/Dfo.Server.Host/appsettings.json)，避免在指南中维护另一份端口表。
配置 `Database:Path` 决定 SQLite 位置；PVF 使用 `PVF_ARCHIVE_PATH` 或程序目录的 `Data/Pvf/Script.pvf`。
组件开关、配置覆盖顺序、预热及缺失／损坏 PVF 的不同处理见
[宿主组件组合](host-component-composition.md)。

## 选择验证范围

```bash
# 与 PR 相同的服务端 Unit 分类；完整 CI 还包含 GM 前端与报告判据
dotnet test Server/86JP.Server.slnx --filter 'Category=Unit'

# 本地完整服务端测试；需按环境提供 PVF
dotnet test Server/86JP.Server.slnx --logger 'console;verbosity=detailed' > /tmp/dnf-server-tests.log 2>&1
```

上面的日志重定向适用于 POSIX shell；其他平台选择可写的日志路径并保留完整 stdout/stderr。
真实 PVF 依赖测试缺少档案时会明确跳过；Host.Tests 含打包进程与真实 TCP 验证。
独立的 [E2E 测试框架与场景台账](E2E/README.md) 必须先有实机采集签收的场景库与报文基线；
当前正式样本为零，入口报告未就绪。`Tool/Smoke/` 的合成库冒烟独立保存，不计正式 E2E 通过数。
全量测试必须保存完整输出、失败用例、异常、栈与最终退出状态，不能用过滤后的摘要替代失败身份。
修改玩法状态的测试经 `Server/tests/Support/TestGameplay.cs` 进入执行器，不能绕运行期保护。
测试选择依据实际行为与风险，类名命中不等于覆盖；检查拒绝点时也检查所有能制造该状态的写入方。

## 失败归因

以下模式用于诊断，不能替代本次失败证据，也不自动免除回归。先取得完整身份，核对改动关联，
再按具体模式隔离复跑；未取到身份的失败只能报告取证缺口。复跑变绿不能单独证明无缺陷。

### SQLite 并行释放

有一条已知的 SQLite 并行释放偶发。**判别依据是三样一起看：**

1. 异常类型 `ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'`
2. 复跑即绿（必须实际复跑）
3. **栈根在下面这份清单内**

用于辨认的栈根：

| 栈根 | 见于 |
|---|---|
| `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar` | `OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`、`DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused`、`DarkKnightComboBookTests.TwoBooksAskedForTheSameCharacterAreTheSameBook` |
| `ItemPathFrameTests.Storage.ReadAsync → SqliteCommand.ExecuteReaderAsync` | 同名用例 |
| `OneDatabaseOneParticipantTests.Fixture.ReadItemsAsync → SqliteCommand.ExecuteReaderAsync` | 同名用例 |
| `AchievementTitleBookTests.Fixture.Sql → SqliteCommand.ExecuteNonQuery` | `AchievementTitleBookTests.Real_title_replaces_mirror_in_one_transaction_and_refusal_or_rollback_preserves_old_wear` |
| `SqliteDatabaseParticipant.CommandAsync → SqliteConnection.BeginTransaction → SqliteCommand.ExecuteNonQuery` | `JoustIntegrationTests.Statistics_failure_rolls_back_mail_completion_and_accounting_marker_then_retry_counts_once` |

该类故障以异常与栈根识别，不能只凭用例名归因。
清单外的新栈根**不得直接归入**，要另给不归因的明证（改动与该路径无交集等），
确认之后把新栈根追加进这张表。

### PVF 随机奖励内容缺失

**身份三样一起看，用例名是其中一样**：

1. 用例 `IllusionTowerPublicFlowTests.Public_handlers_complete_real_pvf_tower_return_to_town_and_reenter`；
2. 异常 `InvalidDataException`，消息含 `[minimum level]` 与 `required field is missing`；
3. 栈根是 `PvfEquipmentDefinitionSource` 缺必填字段时的那次抛出（`PvfEquipmentDefinitionSource.RequiredInt`）。

单独复跑该用例 5 次全过，才归入这一已知模式。用例名是身份的一部分：随机奖励可能选中
PVF 中缺 `[minimum level]` 的装备，导致结算抛错，这仍是生产健壮性缺口。
同一内容异常栈出现在其他用例时，可能是另一处内容缺陷，不能套用上述归因。

### 进程与 socket 超时

Host.Tests 和 Networking 集成测试在满载全量运行时可能因进程退出／连接就绪预算耗尽而报
`TaskCanceledException` 或 `OperationCanceledException`。先单独运行失败项目，区分资源竞争、
取消与行为断言失败；不能仅凭异常类型认定环境问题。保留全量和隔离运行两份证据。

### SQLite 先读后写竞争

两个连接分别开启 deferred 事务、先读同一个库后写时，另一事务提交会使旧读视图无法升级为写事务。
延长忙等待不能刷新视图；默认超时可能使调用方等待约 30 秒后才失败。
如业务允许重试，需要由用例拥有者重新执行整段事务，不能仅重试数据库里的单条写语句。
生产写工作单元通过唯一玩法执行器串行执行；直接操作 SQLite 的并行测试或外部工具仍可能产生此竞争。
现有路径不提供通用 BUSY 重试，不能在排障时假定它会自动恢复。
