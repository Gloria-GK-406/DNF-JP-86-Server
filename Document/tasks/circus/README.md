# #166 疯狂马戏团及天幕剧场

## 范围与状态

- 工作区：`C:\Users\Gloria\.codex\worktrees\fbd8\DNF-JP-86-Server`。
- 分支：`codex/issue-166-circus`；基线 `63fb399345383f0d42d76e4289d8d9f967444acf`。
- 任务：[#166](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/166)，父范围
  [#170](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/170)。2026-10-27 读取两者最新正文，均无评论。
- 用户决定：只实现现行正式图 4103 与天幕 4114；先补真实客户端链路证据，再接入天幕。
- 当前已完成内容读取基础与隔离探查准备；用户已授权恢复默认端口探查并关闭临时占用进程。
  探查服务已重新就绪，沿用现有数据与日志，已启用4103调度开放的单项差分。
  完整玩法、专有准入/计次/奖励/退出及天幕接线尚未完成。
- 用户已授权关停原主干服务，改用原客户端的默认端口；不自动恢复原服务。
  探查入口为 7001、11 频道 10011、登录 HTTP 18080；实际监听与进程见取证准备。
  玩家数据来自原运行库的 SQLite backup API 一致性副本，探查不共写原库。
- 本次交付为关联内容解析基础、协议实机证据和任务探查工具，不是完整马戏团玩法上线；#166剩余范围保留开放。
  探查构建使用基线`0.2.9`；合入代码继承目标main版本，本次基础/研究PR不单独递增玩法版本，也不创建tag或Release。

协议事实只维护在 [flow-circus](../../../protocol/patterns/flow-circus.md)，本目录保留取证材料与验证记录。

## 已实现的内容基础

`DungeonDefinition.LinkedDungeon` 保留前置副本、候选/权重/条件和通关机关；
`PvfLinkedDungeonParser` 只读取对应块，拒绝截断、非法权重、溢出、重复目标/前置与不认识的子段。
普通文件外层的同名 `[next]/[prev]` 不会误生成关联授权。
原始条件尚未解释，解析成功不产生入场许可、随机结果或报文。

状态所有权保持在原有 owner；未向 Networking、执行器或发件箱添加马戏团特判。

## 验证

本轮单人客户端 happy path 已通过，范围与未覆盖项见 [happy path验收](happy-path-acceptance.md)。
该结论限定于受控探查，正式运行的随机抽取/一次性准入仍待接线，尚非完整功能整体验收。

| 检查 | 本次结果 | 证据 |
|---|---|---|
| 真实 PVF 首个关联读取用例 RED | 失败于 `Assert.NotNull(circus.LinkedDungeon)`，证明原实现未读关联声明 | `verification/circus-linked-red.trx`、`evidence/linked-red.log` |
| 对应 GREEN | 1 通过，0 失败，0 跳过 | `verification/circus-linked-green.trx` |
| 关联解析与现有地下城读取回归 | 50 通过，0 失败，0 跳过；包含真实档案与畸形输入/子段作用域 | `verification/circus-content.trx`、`evidence/content-tests.log` |
| 完整服务端解决方案构建 | 0 错误、0 警告 | `evidence/solution-build.log` |
| x86 专用追踪 DLL 编译与替身 ABI 测试 | `/W4 /WX` 构建成功；字段/this 指针保留，登录包未记录 | `evidence/trace-build.log` |
| 独立探查 Host | 构建 0 错误/0 警告，默认端口与登录 API 已核实就绪 | `ProbeHost/`、`probe-plan.md` |
| 取证代理替身验证 | 分片帧保持、未知请求记录、未开启时拒绝注入、注入单独标记 | `verify_probe_proxy.py`；不是游戏实机 |
| 真实客户端入口 | 用户确认气球显示/点击及正式图入场；C2S0024、0010已归档，有限ClientVerified覆盖这段入口 | 见协议流程；033F来源为标记注入 |
| 正式图自然通关 | 用户确认通关成功但未出现凌凌；自然会话未发0282，普通返回请求已归档 | 尚无关联运行接线，不能归因于随机落空 |
| 凌凌/天幕 | 用户确认凌凌实体及首按钮变化，实际0048(2,0)→(1,0)→0010(4114)，四房间及通关已归档 | 单人难度0受控差分；对象直接点击未单测；强制模式已关闭，正常抽取/授权待实现 |

构建采用 `-m:1 -nr:false`。沙箱内并行 MSBuild 图遍历曾无诊断失败，单节点还原成功；
测试宿主在沙箱内未完成运行，受控放行后同一程序集实际执行并取得上述 RED/GREEN。
没有据此更改生产代码或将未执行测试记作通过。

复跑命令（仓库根目录，PowerShell）：

```powershell
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-protocol3\Script.pvf'
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~PvfLinkedDungeonTests|FullyQualifiedName~PvfDungeonDefinitionSourceTests' --logger 'trx;LogFileName=circus-content.trx' --results-directory Document/tasks/circus/verification
dotnet build Server/86JP.Server.slnx -m:1 -nr:false -p:NuGetAudit=false --ignore-failed-sources
python Document/tasks/circus/build_trace.py
```

## 取证材料

- `Probe/`：只读 PVF 摘录与按完整整数 token 查找引用；不把编号子串匹配当作 NPC/物品引用。
- `evidence/dungeons.md`、`entry-content.md`、`town-content.md`、`maps.md`：内容与原生入口候选。
- `evidence/core-actions.md`、`actions.md`、`special-items.md`：客户端脚本机制。
- `probe_binary.py`：mmap 读取、每个关键词最多 40 个命中、每段反汇编最多 4096B、字段引用扫描最多 64KiB；
  目标进程私有内存 384MiB，超过 512MiB 私有字节或工作集立即中止。`linked-reader-confirmed.md` 带实际内存测量。
- `evidence/linked-reader-confirmed.md`：0x0282 接收与 setter 静态路径，尚未证明玩法语义。
- `evidence/server70.jsonl`：具名符号查找的阴性结果及局限。
- `build_trace.py`、`trace_harness.c`：从 ClientHack 当前源文件生成本任务私有追踪变体；源文件/生成文件/DLL 哈希由构建清单记录。
  生成源与二进制在 `artifacts/circus-client-trace/`，不进入客户端仓库；私有副本加载已确认，未覆盖用户原客户端插件。

## 下一阶段

本次PR只提交已验证的内容读取及取证材料；不把代理注入复制到正式Networking、执行器或发件箱。
正常关联抽取、一次性天幕准入和专有计次/奖励属于#166后续正式实现，需要针对性实机验收。

按 [取证准备](probe-plan.md) 确认真实客户端的后续入口与请求顺序；取得足够链路证据后，
再确定正式图计次、后续授权、专有物品、核心机制与结算/退出的实现方案。
自动回归使用本任务独立库。最终功能验收使用届时当前主干实际库，须备份并协调唯一部署时段。
