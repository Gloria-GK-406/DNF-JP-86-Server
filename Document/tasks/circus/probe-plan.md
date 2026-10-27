# 马戏团原生链路取证准备

这是实现前的隔离取证环境，不是功能验收。用户已授权恢复默认端口探查，并关闭此前临时占用进程。
2026-10-27 17:20 核实并关停遗忘之地任务 Host42760 后，启动本任务 Host17484/代理36420。
登录 bootstrap 返回 ready，7001/18080/22001属于Host，10011/22082属于代理。
沿用原探查数据库与日志，不自动恢复原服；最新启动配置和日志路径见 `artifacts/circus-probe/current-run.json`。
已取得凌凌实体显示、结算首按钮继续、4114入场/四房间/通关的受控实机证据；对象直接点击未单测。
用户已选择先补真实客户端链路证据，再接入天幕。

## 当前探查连接方式

用户已明确授权关停原主干 PID 29936，使用原客户端默认端口，并要求不事后恢复原服务。
原主干已退出；活动说明更新后当前 Host PID 10156、代理 PID 3952（均为进程快照）。
已核实 TCP 7001 / 18080 属于新 Host，TCP 10011 属于取证代理；Host 后端 TCP 22001，
代理控制为 `http://127.0.0.1:22082/status`。登录 bootstrap 返回 ready。
原客户端通过原启动器重新登录，选择 11 频道；账号与角色来自原库一致性副本。

运行配置：`artifacts/circus-probe/default-probe.json`。
独立玩家库：`artifacts/circus-probe/default-probe.db`；原运行库位置与已提取的配置覆盖保存在
`artifacts/circus-probe/main-before-switch-20261027-162941/restore.json`。
副本通过 SQLite backup API 取得，停原服务后另取最终一致快照，quick_check=ok；没有替换原库。
原服务实际 PVF 为 `DNFClient-A21-protocol/Script.pvf`，SHA-256 为
`3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B`；
本轮探查 PVF 与用户 protocol2 客户端一致，为流程记录中的 D45C…AEF84，不能混称为原服务档案。

## 已准备的工具

专用 DLL：`artifacts/circus-client-trace/build/Release/0050_A21RuntimeFix.dll`。
构建清单：同一构建根目录下 `manifest.json`。构建入口为本任务 `build_trace.py`。

它由当前 ClientHack 的 0050 源码派生，保留原来的 cipher passthrough、socket staging 与线程修复，
在同一 hook owner 内扩展追踪。不能与另一个 0050 或 0100 NetworkHook 同时加载。
只准备任务独立客户端副本，不覆盖共享客户端、PVF 或插件。

配置项：

```ini
[A21CircusTrace]
Enabled=1
```

输出 `circus-native-trace.jsonl`，包含 UTC、PID、方向、type、完整长度、实际摘录长度、正文 hex 与调用栈 RVA。
记录 000F/0010/0016/001B/001D/001F/0023/0027/002B/0045/0046/0047/0048/0227/0282，
另记录白名单之外的全部出站请求，但不记录 LOGIN/USERINFO 正文。
`apply` 仅标记来自已核实 0x0282 handler 返回点的 setter 调用；其他共用 setter 调用标为 `setter`、type=-1，
不能当作收到了 0x0282。这些行均是运行时观测，不是另一帧。
入站记录没有 selector，必须结合服务端逐帧记录和实际接收调用点判族；零 body/XOR 路径仍有既有观测盲区。

原生替身测试已验证 DLL ABI、记录白名单、原字段/this 指针保留及 hook 位点签名请求。
`build/Release/circus-native-trace.jsonl` 是这个测试产生的**合成样本**，严禁作为实机/线上证据使用。
DLL 已在任务私有原始 DNF.exe 中完成加载，但尚未验证凌凌动作。
用户原客户端没有被覆盖；使用原客户端时，双向帧由探查代理记录。

## 运行条件

已关停的原主干 Host EXE 为
`E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Dfo.Server.Host.exe`；
当前没有可用参照服务，用户选择隔离探查差分。实际取证前重查新 Host 的 PID/监听。

默认隔离候选端口使用 22000–22999；用户本次明确要求接管默认端口，配置以显式 Research 标记允许
7001 / 10011。先核查 TCP/UDP、PID、EXE 与
`channel_info.etc` 最终派生的 `10000 + channel.Id`，使用本任务资源副本。
候选范围在本次快照中未见监听，不等于已预留。
探查使用独立库；不与主干或其他会话共写玩家库，也不停止现有服务。

自制探查 Host 的帧只能证明本次差分下的客户端反应，不能冒充原版服务端 WireCaptured。

代理使用 `POST /arm` 的 `{session, enabled}` 开关正文记录，`POST /mark` 标记动作，
`POST /0282` 接受 `{session, first, second}` 手工差分，明确写为 injected。
显式启用 `--circus-schedule-on-select` 时，在选角 EVENT_INFO 后标记注入一次
033F `(0,1,4103,0)` 以验证气球显示门，并开始记录后续非登录/USERINFO帧。
代理3952曾按用户授权以`--force-circus-linked`启动临时100%差分：
仅对本次4103入场，在0023结算后标记注入一次0282候选`(4114,0)`，用于检验上游字段假设。
成功验证后已执行`POST /scenario {force_circus_linked:false}`并读回关闭状态；PVF原始50/50权重未修改。
当前运行时开关为false；后续重启不带`--force-circus-linked`，不得照抄旧进程的该启动参数。
没有继续授权、转场或 EPLP(1,3) 处理。未注册请求仍交给原服务端路径记录和拒绝。
气球显示条件与本次差分边界见 [NPC显示门取证](npc-visibility.md)。

## 编号观察顺序

1. 记录任务客户端 EXE/PVF/插件 SHA-256、进程 PID/EXE、后端提交/配置与实际监听；确认独立日志输出。
2. 由暗黑城原生入口进入马戏团，记录区域 64、gate 103、000F 与 0010 的实际顺序/正文；
   留下界面反馈，确认只出现正式图而非新增入门关。
3. 观察追猴子区域触发、90 秒脚本分支、滚球、皮埃罗反伤/销毁与麻醉剂的原生行为，
   同时记录哪些请求实际到达服务端；未执行的分支保留 Unknown。
4. 击败狮子王，记录通关、翻牌、0x0282 的时序、两个值及 setter 调用；区分自然出现与人为注入。
5. 若原生天幕入口出现，点击一次，记录 0048、000F、0010 或其他真实请求，确认是否需要服务端主动转场。
6. 确认载入 4114、原生机关及返回；第二次点击/退出重登只作观察，不据一次成功推断全边界幂等。
7. 将有明确来源/哈希/方向和动作的原始片段归档到 protocol，分别更新 ClientStatic、
   WireCaptured、实机差分和 ClientVerified；只有链路闭合后再实现授权及转场。

取证时段、连接方式和客户端启动须与用户协调；纯等待操作不作为 continuation todo。
