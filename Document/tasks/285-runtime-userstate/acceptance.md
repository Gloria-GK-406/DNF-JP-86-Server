---
kind: rks-verification
decision: manual_required
---

# #285 实时副本身份刷新与实机验收

代码修复、相关自动回归与可运行候选准备完成；本次实机尚未执行。授权来自本次明确实施 #285 的请求。任务分支 `codex/285-runtime-userstate`，主干基线 `f89a8bb0b`（已含 #281）；只移植 `2a8fde3ac` 中相关代码及回归，不合并 E2E 分支，不修改版本号。实机通过前不建 PR、不合并、不发布、不关闭 #285 或 #178。

## 修复与覆盖入口

`CharacterCurrentStateQueryDomainService` 与 `ReplayStateDomainService` 共用 `CharacterStateProjection.CompleteRuntimeIdentityProjection`，读取权威 `IDungeonParticipationDomainService`，仅在输出中投影副本状态。现行规则见[当前视图](../../SelectedSessionCurrentView.md)；字段及本地换房判断的证据只维护在[NOTI0002](../../../protocol/game/server-to-client/NOTI-0x0002.md#完整刷新中的实时-userstate285)与[CMD002D](../../../protocol/game/client-to-server/0x002D.md#完整刷新与本地换房前置条件285)。

调用核查结果：

| 消费入口 | 当前事实来源 | 本次验证边界 |
|---|---|---|
| MoveItemSpace 换装 | 当前视图 → CharacterUserStateProjection | 共享查询字节回归；真实装备命令链 ACK/刷新顺序回归 |
| CharacterUserStateRefresh 通知 | 当前视图；对接收者及 owner 检查会话 | 源码调用核查；多人新实机待测 |
| TitleBook 称号领取／穿戴 | 当前视图 → CharacterUserStateProjection | 源码调用核查；本次独立实机待测 |
| ExpiredItemsRefresh 到期装备 | 当前视图 → CharacterUserStateProjection | 源码调用核查；本次独立实机待测 |
| QuestCompletion 转职刷新、LevelUpTicket | 当前视图 → Current/ExtendedUserStateMessage | 源码调用核查；本次独立实机待测 |
| ReplayStateProjectionDelivery 外观重试 | ReplayStateDomainService → 共享身份投影 | 真实 SQLite 快照、内外两种状态与编码字节回归 |
| 选角完整同步、城镇在场发布 | 选角准备、城镇领域各自的原有入口 | 保持原有边界；不宣称副本多人已实测 |

## 自动验证

完整日志保留在任务工作区 `.task-evidence/285/`，不提交含现场数据的目录。所有本表通过项均无跳过。

| 检查 | 结果 | 日志 |
|---|---|---|
| 全解决方案 Release 构建 | 0 警告、0 错误 | build-release.log |
| 当前视图与公开投影 | 21 通过 | green-current.log |
| 重试快照、当前角色事实、真实装备命令链、依赖环 | 42 通过 | green-application.log |
| Host 架构／生产组合 | 19 通过 | host-architecture.log |
| 装备协议旧组 | 47 通过、24 失败；未修改主干也是同一失败集合 | green-protocol.log、baseline-main-move.log |
| 协议目录对账 | 当前分支与未修改主干均433项解析失败，差集为空；不是通过 | protocol-sync.log、baseline-protocol-sync.log |
| 私有诊断候选启动及双向采集 | 动态端口、单独 smoke/run.db；收到初始 S2C、C2S04DD 及发送成功记录；自己的进程已结束 | smoke/result.json、smoke/host.log |

行为 RED：在原 `35049db76` 基线加上测试及注入接线但保留原输出行为，普通/幻影/Boss 的3项编码检查全部“预期1、实际0”；重试副本内同样失败，副本外通过。日志 red-current.log、red-replay.log。GREEN 还覆盖无参与关系保留资料值2、离开副本后恢复资料值、原资料未改、重试编码状态及 SQL user_state 不变。

24项旧失败均在 `MoveItemProtocolTestDriver.Units.WriteAsync` 的 `Assert.Empty(d.Participants)`，与当前生产命令已经携带参与者不一致，尚未进入装备执行。两份日志逐项对比失败身份完全一致；未放宽旧断言，不能把该组说成全通过。真实 `MoveItemSpaceProtocolCommandFlowTests` 的 wear/takeoff 等路径通过，验证成功 ACK、既有帧顺序与装备资产；不是实机证据。协议对账脚本只扫描 Protocol 中的常量，而若干身份枚举已在 Data，导致包含 ClientMessageType 在内的解析失败；本任务没有新增 decoder/encoder，未扩展为修复该工具。未运行全量解决方案测试或 Linux CI。

可复跑命令（仓库根，PowerShell；PVF 使用下述服务端文件）：

```powershell
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-protocol\Script.pvf'
dotnet build Server/86JP.Server.slnx -c Release
dotnet test Server/tests/Dfo.Server.Protocol.Tests --filter 'FullyQualifiedName~SelectCharacterProductionStateTests|FullyQualifiedName~CharacterUserStateProjectionTests'
dotnet test Server/tests/Dfo.Server.Application.Tests --filter 'FullyQualifiedName~ReplayStateSnapshotTests|FullyQualifiedName~CurrentCharacterStateTests|FullyQualifiedName~MoveItemSpaceProtocolCommandFlowTests|FullyQualifiedName~DependencyGraphArchitectureTests'
dotnet test Server/tests/Dfo.Server.Host.Tests -c Release --filter 'FullyQualifiedName~ArchitectureTests'
```

## 现场、运行产物与数据库身份

准备时只读复核：无 DNF.exe / Dfo.Server.Host.exe 进程，7001、10010、10011、10100、18080、18081 未监听；#281 host.log 尾部为2026-11-07 20:20。该观察有时效性，启动脚本会重新拒绝端口占用或其他 Host。

以下路径以任务工作区 `C:\Users\Gloria\.codex\worktrees\issue-285-runtime-userstate\DNF-JP-86-Server` 为根：

| 对象 | 位置／身份 |
|---|---|
| 测试 Host | `.task-evidence/285/server/Dfo.Server.Host.exe`；本机 .NET 10 framework-dependent 构建 |
| 测试库 | `.task-evidence/285/server/Data/dfo-server.db`，schema v3 |
| 一致备份 | `.task-evidence/285/backup/281-consistent-before.db`；SHA256 `f6586f9c19bed19073a1e6be12f7c6aa6e653718536d2aa883ed6e09cf517549` |
| 数据来源 | `E:\Project\DNF\.task-evidence\281-dragon-weapon\server\Data\dfo-server.db`；只读 SQLite backup，包含已提交 WAL；integrity_check=ok |
| 测试角色 | account_id=4、character_id=2、level=86、资料 user_state=0、tutorial_completed=0；未补造任务／装备或修改人物数据 |
| 主目录旧库 | `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`，只读检查为v0；不迁移、不写入，不将其称为当前在线库 |
| 产物身份 | `.task-evidence/285/manifest.json` 锁定逐 DLL 哈希；Application 包含 CompleteRuntimeIdentityProjection |
| 生产 Networking 备份 | `.task-evidence/285/production-networking/Dfo.Server.Networking.dll`；测试副本使用下述诊断组件 |

诊断候选的 Application/Protocol/Host 来自本任务；仅 Networking 在私有源码副本中复用 `db6f578d0` 的4文件传输录包差异（Capture/NetworkFrameRecorder、NetworkingModule、OutboundTransport、NetworkPlugins），不把 E2E 工具或此诊断差异提交进修复分支。GM 静态资源复用 #281 产物中的 wwwroot。该候选基于已合入 #281 的当前主干，但本任务只验收公共刷新问题，不替代或扩展 #281 的验收结论。诊断源码及构建日志在 capture-src/、build-capture.log，可复核。收发记录是应用传输边界的完整帧与 send-result，不是网卡层 pcap。

客户端：`E:\Project\DNF\DNFClient-A21-protocol2\DNF.exe`，SHA256 `1f8244d3f7309bbe2fde4299e1863a894f6b28d5707bf01732980585b20b00dd`。现有客户端 PVF SHA256 `d45cf58b1a2b3d16d6b35abe0d4e302e052f0b6bc43e9d3e98d815fed54aef84`；服务端仍使用 `E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，SHA256 `01f1be685d46647961df65207a36304777db20ef5a3d03c68efaa92f8b0c5189`。保留现有素材，不修改客户端；两者不一致，本次仅限该环境中的故障验收，不是固定素材正式 E2E 基线。

## 切换、采集与恢复

候选尚未在共享端口启动。用户确认可以切换后由代理执行：

1. 再核实客户端和全部 Host、端口；若有其他任务占用，先协调，不终止其进程。
2. 运行 `.task-evidence/285/start-test.ps1`，开启 Launcher 18080、GM 18081、Channel 7001、Game 10010/10011/10100；关闭 UDP discovery，使用配置频道列表10/11/100。启动隐藏窗口，脚本检查逐 DLL 哈希，唯一写库路径是本任务测试库。
3. 检查启动日志、bootstrap 和监听归属，确认进程实际路径及数据库。每次录包新建 `runs/<时间戳>/frames.jsonl`，同步保留 host.log、host.err.log 与 active-run.json。原始流量可能含票据，私有保留不入仓。
4. 用户按下列编号操作；每个阶段反馈完成／异常及时间，由代理保存一致数据库快照、录包前缀与哈希，解析 UserState、002D、START_MAP，核对发送成功。
5. 收尾先退出客户端，再运行 stop-test.ps1；它只结束匹配可执行路径和启动时间的本次进程。强制收尾后保留 WAL/SHM，读取或恢复数据库必须用 SQLite backup，不裸拷贝正在使用的db文件。
6. 恢复 #281：确认本次 Host 已停、端口空闲后，使用原 `E:\Project\DNF\.task-evidence\281-dragon-weapon\start-default-ports.ps1` 启动其原 server 与 Data。本次从未覆盖其产物／配置／库；不把任务测试库写回 #281 或主库。需要重做 #285 时，在本任务停服后从一致备份生成全新测试库并另存本轮测试库，不覆盖取证资料。

## 按顺序进行的实机步骤

1. **城镇基线**：使用现有账号登录普通频道10，选择 character2；确认装备与物品正常。代理记录城镇 basic USERINFO 状态及库身份。
2. **dungeon93 同类场景**：从当前合法任务／入口进入与现场一致的普通副本（现场为Cycle、quest14836）。先正常清怪并换房一次。若任务已推进或入口不可用，反馈实际情况，由代理核实；不直接补任务／改库模拟。
3. **武器换装与连续换房**：在非Boss可换房位置换上 item101000043（现场Main35→Equipment12；以本轮实际槽位为准）。确认装备成功，然后正常清怪，连续通过至少两次合法房门。代理必须取得同一连接上的 CMD0013、成功ACK、其后的 NOTI0002 subtype0 UserState=1、后续 CMD002D 与 START_MAP，并核对刷新装备内容和发送成功；用户确认画面确实换房。
4. **其他装备**：在副本内再换一件可穿装备或称号，重复合法换房；记录本轮物品和实际槽位。特殊职业／装备限制导致拒绝时保留真实结果，不计为刷新成功路径。
5. **回城与重进**：正常返回城镇，确认状态恢复、装备正常；再次进入合法副本，换装并继续换房。若Cycle任务推进已改变场景，分别记录实际副本与模式，不冒称同一quest重放。
6. **训练护肩**：通过正常注册／建角流程创建新的测试角色，进入训练；第二房拾取并穿护肩后继续过图直至训练结束回城。仅在本任务测试库操作。保存换装后的state1、后续002D/START_MAP和用户观察。
7. **训练后重登**：正常退出再登录该训练角色，确认直接进入城镇且未重进训练。此步本次仍须验证，既有 #212 通过不能代替。
8. **扩展边界**：有第二客户端时验证副本内队员换装后本人仍能换房、同伴外观正常；另行触发称号领取、装备到期和外观重试时分别记录。缺少现场条件可明确未测，不能将单人结果扩大为多人或所有刷新源已通过。

## 未验证条件

本次所有实机编号均待用户执行；普通副本的完整覆盖瞬间尚无新录包。幻影/Boss身份只有自动化编码验证；多人和其他刷新触发源目前只有调用核查。未取得当前实机反馈前，不声明问题已 ClientVerified，也不创建 PR。
