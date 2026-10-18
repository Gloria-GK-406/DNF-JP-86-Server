# 祭坛放弃回城死锁修复（2026-10-13）

用户确认莫尼亚一直用火属性攻击，先按原生冰属性机制重测；本次授权为“先处理无法回城的问题，我再重跑”。沿用`task/next-round-20261013`，基线`89f67c4bbc0055c4ff3bfca84d08bf000dd06958`。采用Direct局部修复，不修改已封存的祭坛Spec。

## 根因与改动

CMD002A的GiveUpGameHandler经异步AcquireGiveUpAsync获取SelectedSessionTransitionGate，再把lease返回调用者。锁的AsyncLocal Frame只存在于辅助函数ExecutionContext，调用者中的AltarActionCommand进入真实SelectedSessionExecutionBehavior后不能借用它，只能等待已由自身持有的锁。结算无法开始，ACK、回城及同账户断线清理被阻塞。

将实例projection gate和有序账户transition gates直接在处理器当前执行流获取，删除异步返回lease的辅助函数。保持实例锁→账户ID排序锁、会话/Run重验、先结算后移除Run、finally逆序释放的既有职责。普通地下城仍只取请求者账户锁。

上下文审阅确认异常/取消/提前return均经finally释放已取得的lease，普通地下城和祭坛共用退出行为不变。这是执行者的局部审阅，不称为独立审查。

## 回归与证据

原测试使用RecordingCommandBus，没有真实会话借锁，因此没能保护此缺陷。本次替换为真实CommandBus及完整命令管线，业务边界由小型处理器隔离；测试观察002A ACK、城镇位置刷新、Run/收件人清理、重复请求不重复结算，以及后续能重新取得会话锁。

- RED：`dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Give_up_completes_through_real_command_locks --logger trx;LogFileName=red.trx --results-directory artifacts/altar-return-fix-20261013/verification -v quiet`。旧实现祭坛分支在3秒取消后失败，栈为SelectedSessionExecutionBehavior→CompleteAcquisitionAsync；普通分支通过，exit1。TRX位于本次artifacts下`verification/red.trx`。
- GREEN：相同命令改LogFileName=green.trx，两个分支均通过，exit0。
- 最终协议全量：显式设置`PVF_ARCHIVE_PATH=E:/Project/DNF/DNF-JP-86-Server/artifacts/avatar-socket-text-20261011/Script.server.pvf`，运行同项目Release `--no-build`全量，1485通过、0失败、0跳过，`verification/protocol.trx`。
- Application专项：同一PVF环境，filter为`FullyQualifiedName~Altar|FullyQualifiedName~SelectedSessionTransitionGate|FullyQualifiedName~SelectedSessionExecution`，28通过、0失败、0跳过，`verification/altar-session.trx`。
- 首次协议全量因未设置PVF而跳过60项，已由上述显式PVF的最终全量结果替代。没有把跳过当作对应内容通过。
- Release publish成功。自动化只证明回城处理及锁行为，客户端实际回城仍待用户确认；木乃伊、客户端和PVF本次没有变化。

## 部署身份与回滚输入

运行目录`artifacts/altar-return-fix-20261013/runtime`，PID13224，版本`0.2.5-altar-returnfix.20261013`。InformationalVersion包含发布时基线89f67c4b，不冒充后续本地结果提交；实际源码由source-manifest.json逐文件核对，manifest SHA256 `3e4b169a3d9a38e0ed55925278434a32bbe665b2300230d549d5c313b988655b`。

Protocol DLL SHA256 `C00E1EE154161BC5A1168ADCA4BD7318618138BBE5A3F3AA10280835215EF657`。登录bootstrap返回ready，GM HTTP200；31个预期监听端口均归新PID，缺失0。旧PID39268已停止。原始身份与监听记录在deployment-verification.json。

旧运行目录`artifacts/altar-dungeons-20261013/runtime`保留，新artifacts/rollback下保存appsettings.previous.json和database-before-return-fix.db。数据库backup API生成的备份及当前库integrity_check均ok，sqlite_master相同，无破坏性迁移。配置SHA256仍为`96370E95F7F834D8EEF68E39F86C2C5FC5772CA23CC21B96CC3646226F8E1869`，保留现有角色、设置及每日计次，不用旧库覆盖新进度。

建议用户重登后先在祭坛中途放弃一次，确认能回城并再次进入；再用冰属性处理莫尼亚，验证第6轮原生机制和最终结算。工作分支保留，无PR、合并或分支清理。
