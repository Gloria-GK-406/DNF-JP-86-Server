# CMD 0x0007 返回选人实现

实现分支：`main`。协议处于“双向验证结束（A21）”，部署后两轮返回选人并再次进入通过，用户已确认。

## 行为

新增 `Game/ReturnToSelection` 切片并通过 `ProtocolModule` 注册。空体请求返回 `CMD 0x0007 / 01`，
随后发送最新角色名单和荣誉信息；守护者先收到五槽空盾牌栏。账号与 TCP 连接保留，角色会话释放。

准备命令在账号独占事务中保存既有脏库存并查询当前穿戴名单；完成命令复核连接代数和精确角色会话，
结束地下城运行，再复用逐项清理服务保存位置、撤销场景存在、关闭摆摊、释放库存会话与活动角色。
完成命令使用 Workflow，防止清理汇总异常将已经成功落盘的位置一并回滚。

handler 按在线用户门 → 角色生命周期门的顺序保护整段返回；两个命令各自取得账号门。
失败路径不发虚构成功，清理异常携带的通知仍尝试投递；需要关闭连接时先释放门，避免断线回调等待自己。
若准备阶段保存失败，命令本身不释放角色；handler 随后按失败路径关闭该连接。

跨协议投递组合放在 Game 端点的 `CharacterSelectionDelivery`，新切片没有直接引用名单、荣誉或入场同步切片。

## 自动验证

- 真实 TCP：登录、读取登录尾批、发送空体 `0x0007`，收到成功信封、subtype 2 名单（header byte 7=0）、荣誉信息；连接仍认证。
- 生产状态清理：运行目录、库存会话、位置、场景存在、摆摊、角色选择及地下城记录均释放，位置已保存；再次调用选人 handler 可进入。
- 新名单与入场快照不同，测试要求返回查询最新外观；旧请求不能清理新代角色；库存保存失败不提前注销角色。
- 编码注册：普通角色三帧、守护者四帧及五个零盾牌槽；不接受带额外 body 的请求；准备/完成命令分别注册为 Transactional/Workflow。

全套执行：

```powershell
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-protocol\Script.pvf'
dotnet test Server/86JP.Server.slnx --artifacts-path E:/Project/DNF/.return-selection-build --verbosity quiet
```

结果：2423 通过、0 失败、4 跳过。两项跳过是独立输出目录无法定位 Host 源配置文件，另外两项是环境可访问 PVF 时
主动跳过“缺失 PVF”用例。全套输出：`E:/Project/DNF/.return-selection-build/full-tests.log`。
两个配置检查另在原输出目录用 `--no-build --filter` 执行，避免覆盖正在使用的 Host DLL；两项均通过。
合并这两项补查后，2427 项中 2425 通过、0 失败、2 项因 PVF 可用按设计跳过。

## 证据与实机

- [协议字段记录](../protocol/game/client-to-server/0x0007.md)
- [发送流程与实机验收](../protocol/patterns/flow-return-selection.md)
- [客户端哈希、指令及固定远端提交](../protocol/patterns/data/probes/a21-return-selection-20260908.md)

2026-09-08 23:20:38 按用户要求重启，运行实现提交 `94a243f6` 的新构建，PID `47028`。
Host 构建 0 警告、0 错误；7001 / 10010 / 10011 均已监听，启动日志无错误，继续使用原数据库。
日志：`Server/src/Dfo.Server.Host/bin/Debug/net10.0/Logs/server-20260908-232038.stdout.log`。
真实客户端已完成两轮返回选人并再次进入，用户反馈测试无问题。
[实机记录与覆盖范围](../protocol/patterns/data/traces/a21-return-selection-live-20260908.md)；未逐字段确认外观或覆盖所有职业及错误分支。
