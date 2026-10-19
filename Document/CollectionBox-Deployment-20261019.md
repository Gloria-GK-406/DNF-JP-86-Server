# 收集箱部署与实机测试

后续更新：收集箱说明已同步常驻版PVF，当前PVF与进程身份见[PVF同步记录](CollectionBox-PVF-20261019.md)。下文保留首次程序部署时的收据。

日期：2026-10-19；最终运行核验时间：`2026-10-19T12:09:14.7939826+08:00`。

用户授权：“合并回 task/next-round-20261019 并清理收集箱工作区，然后部署。我测试看看效果”。已完成本地合并、清理和运行实例替换，原生客户端效果待用户测试。

## 当前可测试版本

- 分支：`task/next-round-20261019`，从基线bf283b86快进到`ca0e62fe6507851a9a8c13b90802c7e7a68baf19`，保留规划7ca2d8a7、实现024acb76、封存ca0e62fe三个提交的顺序与内容。
- 已移除Git注册的`.worktrees/collection-box`和已合并本地`task/collection-box`分支。实现、审核、测试日志与验收说明已随合并保存在主工作目录，原有18个未跟踪文件逐一核对哈希保持不变。
- 运行版本：`0.2.7+ca0e62fe6507851a9a8c13b90802c7e7a68baf19`，PID `40068`。
- 程序：`E:\Project\DNF\DNF-JP-86-Server\artifacts\collection-box-deploy-20261019\runtime\Dfo.Server.Host.exe`。
- Host DLL SHA-256：`D11FE1790F55D854BDE8A5B72F9E49F019766ABDC6ACEC9CBEF83B7A39F2E500`。
- Protocol DLL SHA-256：`44FBA317ACD149485930CC571C7757BA0ED07F1E9E3D693C9BC123FF51FA29D0`。
- PVF：`E:/Project/DNF/DNF-JP-86-Server/artifacts/wedding-ticket-pvf-fix-20261018/Script.server.pvf`，SHA-256 `BCF99E4E4CC691D43DD89373519EF67D632B83FB0BDAD560454E101415768D63`；沿用现有资源与运行配置。
- 数据库：`E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`。

## 验证证据

合并后的应用相关90项、协议投影24项、持久层34项全部通过（各命令exit0）；Release发布exit0。原完整实现的全量回归与独立审核见[已封存的软件验证](tasks/collection-box/verification/completion-evidence.md)。本次没有修改生产源码。

- 在线备份及停服后备份均通过`PRAGMA integrity_check`和`foreign_key_check`。
- 使用新发布Host在备份副本中禁用网络演练，原105张表的逐表内容指纹一致，只新增空`character_collectbox_state`。
- 实际替换旧PID `42932` 后，启动前后的原105张表指纹仍全部一致，新表为空。
- 最终独立核对实际进程路径、12个程序集版本与SHA-256、PVF哈希；31个TCP与2个UDP监听和旧实例集合一致，归属新PID。
- Launcher bootstrap和GM health均ready，GM页面HTTP200，stderr为空。

收据、完整哈希与核验日志位于`artifacts/collection-box-deploy-20261019/`：`source-manifest.json`、`deployment-receipt.json`、`final-runtime-verification.json`、`post-start-comparison.json`及`post-merge-*.log`。该目录独立于已清理工作区而保留。

## 备份与回退边界

- 配对回退数据库：`E:\Project\DNF\DNF-JP-86-Server\artifacts\collection-box-deploy-20261019\stopped.db`，SHA-256 `16651FBC075AD533FD6E4A1F0B2FEBFD90D542C0AA44EBB822E1596D56606003`；线上停写后的完整一致快照。
- 配对旧程序：`E:\Project\DNF\DNF-JP-86-Server\artifacts\marriage-capsule-20261018\runtime\Dfo.Server.Host.exe`，版本`0.2.5-marriage-capsule.20261018+4f328aeec1e32a30ec8ddf0939cef0cfd851c5f0`，原目录保留。
- 另保留在线`before-stop.db`、副本演练`schema-rehearsal.db`和启动后`post-start.db`，以及原配置副本。
- 旧版不能直接读取出现241容器的新库。用户测试产生新进度后，恢复停服前备份会丢弃之后的进度；需要另行决定和处理，不能只换回旧程序。

## 实机测试

重新登录后，按[收集箱验收步骤](CollectionBox-Implementation-20261019.md)检查三套系列的常驻显示、存取、切换属性与重登恢复，保留双向抓包。当前仍为CurrentImplementation + ClientStatic；运行健康和自动回归不等于ClientVerified。实际PVF文案没有替换。
