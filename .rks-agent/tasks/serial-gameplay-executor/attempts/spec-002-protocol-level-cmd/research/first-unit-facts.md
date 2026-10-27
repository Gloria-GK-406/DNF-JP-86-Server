# 首单元只读事实（2026-10-24）

独立采集b_f130_cleanup_facts、b_f127_transport_facts。未修改产品/测试、未运行测试/服务；以下触发方案不是已成功复现。用于临近Task计划，不是新增Spec硬机制。

## F130：App25个进程级清池点

生产SqliteDatabase写串仅DataSource=Path.GetFullPath(path)，Participant从该串加Mode=ReadOnly，别加FK/Cache/Pooling或ResolveLinkTarget。驱动10.0.10 Factory按原连接字符串精确分组；构造/ConnectionString setter已绑定池，ClearPool(new未Open连接)有效。

官方精确源：[Connection](https://github.com/dotnet/dotnet/blob/f7d90799ce4ef09a0bb257852a57248d2a8fb8dd/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs#L124-L215)、[Factory](https://github.com/dotnet/dotnet/blob/f7d90799ce4ef09a0bb257852a57248d2a8fb8dd/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnectionFactory.cs#L45-L80)、[Pool Clear](https://github.com/dotnet/dotnet/blob/f7d90799ce4ef09a0bb257852a57248d2a8fb8dd/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnectionPool.cs#L94-L113)。

路径相对Server/tests/Dfo.Server.Application.Tests；IO表示仅吞删除IOException，原清池均在catch之外，不扩大吞错。

| 文件:行 | 自有输入 | 释放/删除边界 |
|---|---|---|
| Authentication/GameLoginCommandTransactionTests:78 | databasePath=directory/game-login.db | using services先释放，删目录无catch |
| Characters/CharacterDomainStoreSettlementTests:325 | this.Path | 局部连接结束，删文件IO |
| Characters/CharacterItemUseTests:471 | directory/characters.db | 路径宜提升try外复用，删目录无catch |
| Characters/CharacterSettingsTests:183 | this.Path | 删文件IO |
| Characters/CollectionBoxTests:203 | path | await Services.DisposeAsync后，删db/wal/shm，无catch |
| Characters/DarkKnightComboBookTests:386 | this.Path | 删文件IO |
| Characters/OutsideTransactionTests:311 | this.Path | 删文件IO |
| Creatures/CreatureRuntimeDomainServiceTests:625 | this.Path | 删文件无catch |
| Creatures/CreatureTransformationTests:217 | path | 连接结束，删文件无catch |
| Events/SaoTowerEntranceEventTests:91 | directory/test.db | Services.Dispose；Reopen owns=false应先返回，只有owner清池删除 |
| Execution/GameplayExecutorLifetimeTests:139 | directory/test.db | using host先释放，删目录IO |
| Execution/TimerSourceHost:85 | directory/timer.db | Stop→provider.DisposeAsync→清池，删目录IO/UnauthorizedAccess |
| Game/CreatureGoesBackOnTests:162 | path | content.Dispose后，删文件IO |
| Inventory/CreatureKindAgreesWithTheArchiveTests:127 | path | 连接结束，删文件IO |
| Inventory/DraggedAvatarStaysWholeTests:384 | path | 删文件IO |
| Inventory/GrantedAvatarIsWholeTests:286 | path | 混合283CRLF/297LF，字节级替换保行尾，删文件IO |
| Inventory/MovingInsideTheAvatarListTests:197 | path | 删文件无catch |
| Inventory/PickedUpAvatarStaysWholeTests:393 | path | 删文件IO |
| Mail/MailboxCoreIntegrationTests:649 | directory/mail.db | provider先释放，已有!ownsDirectory return，owner清池删除 |
| Marriage/MarriageArchitectureTests:151 | path | Provider.Dispose后，删文件无catch |
| OnlineUsers/SerialDisconnectTests:91 | directory/test.db | await using provider先结束，fixture删目录IO |
| Quests/QuestLogTests:727 | this.Path | 删文件IO |
| Transactions/DungeonPickupTransactionTests:97 | path | await using provider先结束，删文件IO |
| Transactions/OneDatabaseOneParticipantTests:277 | this.Path及this.OtherPath | 两库各写/只读池都清；分别删文件IO |
| World/PaidCardFlipCostTests:565 | this._path | Discard自有库，删文件IO |

TimerSourceJobFailureTests:129也调用SqliteTestFiles.Delete，实际timer.db，host在外层finally前释放。helper应显式给库路径不猜目录。App本地namespace Dfo.Server.Application.Tests可被子namespace解析，无需外扩程序集。当前builder与手写Data Source串并用，需覆盖实际原文串变体或统一这些测试连接串构造，不能只靠同文件语义宣称清全。Scope只App25，不扩其他进程92文件。

验证建议：A/B各写/只读空闲池，保存公开Handle，先Close归池；清A后A两Handle.IsClosed、B两Handle未关且可重开查询。全局清池会关B、只清写会漏A只读、漏GetFullPath会漏A。只看B活跃连接还能查不够（全清可能仅标不可返池）。此为清理隔离证据，不证明历史全部SQLite偶发根因。

## F127：失败时写端状态，不改Resolve

当前OutboundTransport.cs64-73 Resolve只exact+Online；81-91是真实写。OutboundSender.cs213/225两失败、235 gone；ProcessAsync125-132的Resolve-null会Stale/close:false，不能改其整体语义。

pinned TouchSocket4.3.1本地DLL IL：BaseTransport.ClosedEventArgs getter字段空也返静态default对象，非null不可作关闭；TcpTransport.RunSend finally IL023A～0245 complete sendWriter但无SignalClose，receive到03AC～03B1才SafeCancel；TcpSessionClientBase.RunSessionAsync transport.SafeDispose后014A才m_online=false；ProtectedSendAsync先Online再用transport.Writer，检查与写不是原子。

可行边界：IOutboundConnection提供失败分类能力（如HasClosedSendSide(error)），只在原send catch与Resolve gone合并并捕获日志事实；TouchSocket adapter用ClosedToken+严格已知writer-completed InvalidOperationException形态补充，保异常原对象，不在通用sender按所有InvalidOperationException降级，不改Timeout/Failed/close/observer。ClosedToken单独不覆盖writer先完成窗口。具体API留Task。

[.NET10.0.10官方ThrowHelper](https://raw.githubusercontent.com/dotnet/runtime/v10.0.10/src/libraries/System.IO.Pipelines/src/System/IO/Pipelines/ThrowHelper.cs)确认NoWritingAllowed源；TouchSocket精确线上commit未取得，事实来自本地pinned DLL，不能冒称在线源已核。

真实传输候选：最小RealOutboundServer.Received用TCS阻住Session receive callback，取得真实TcpSessionClientBase.Transport，await transport.CloseAsync后Online仍true/Resolve仍真、ClosedToken取消；经sender发消息进入真实已完成PipeWriter，验证两Info、原异常、Failed/后继discard/observer一次/另一连接成功，finally放Gate并等生命周期。另一行只Complete真实transport.Writer而token保持false，守先完成窗口。保普通故障/超时Warning对照。夹具OutboundRealSocketTests:341-407可扩；尚未实际运行，执行要先RED/GREEN。
