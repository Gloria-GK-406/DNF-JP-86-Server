---
kind: rks-plan
contract:
  path: ./TaskContract.md
  revision: 1
spec: ./spec.md
mode: direct
state: ready
---

# Plan: 固定 A21 原生公共队伍目录运行时闭环

## Planning route

采用 `direct`：这是一个有严格先后关系的单次受控运行，Profile 的 preflight/implementation/postcheck 已提供
恢复边界；另建 Task Units 会把同一 exact-session 时间线拆散，并不会增加独立交付或回滚价值。

## Detailed work

### T-001 — 只读预检与冻结边界（RQ-001）

- 核对 worktree/branch/accepted baseline，Release Host 与数据库实际路径；哈希两份客户端的 DNF.exe、
  ClientHack/插件/配置、B1/dstr/PVF 和 Host 产物，并与固定 A21 静态证据核对。
- 检查 DNF/Host/既有中转进程及 `7001/10010/10011/17001/18080/20010/20011`；任何非本轮 owner 占用都
  不终止，先记录为 prerequisite blocker。
- 只读取两份 launcher 的独立客户端目录与登录标识，最终证据只记录不可逆 hash/角色 ID，不保存密码。
- 确认 x86 目标的只读 `ReadProcessMemory/VirtualQueryEx` 能力、final vptr RVA `0x02DFFFD4`、ID `0x11`
  vector 对象 `this+0x188`（begin/end/cap 为 `+0x18C/+0x190/+0x194`）和可用的诊断 fallback；
  preflight 不启动 Host/客户端或改配置。

### T-002 — 构建并自检最小观测面（RQ-001、RQ-002、RQ-005）

- 在本 Spec `evidence/runtime/` 新建任务专用定向 TCP proxy，复用已接受的 11/14/15-byte frame 重组，
  Channel 透明且不落盘；Game 只允许 C2S `0x0004/0x000C/0x000D` 与 S2C NOTI
  `0x0002/0x0006/0x0009`，记录时间、listener、connection、方向、selector/type、length/body/frame。
- 先写 split/coalesced、方向/selector 白名单、敏感 Channel 排除和顺序保真测试，再实现最小扩展并运行
  self-test；原始会话输出写新文件，不覆盖历史证据。
- 新建只读 observer state probe：按 executable path 等待 `DNFClient-A21-protocol2/DNF.exe`，扫描当前
  moduleBase+final-vptr-RVA 的 ID `0x11` 实例，校验 vector begin/end/cap 的可读性和有界长度，记录
  instance、partyId 集合与状态变化。探针不得申请写/操作/注入权限；以合成内存解析和拒绝无效指针判据自检。
- 若轮询扫描不能可靠定位，fallback 仅用同一只读 API 生成任务临时内存快照并离线扫描 final vptr/vector，
  或在工具后来可用时用 debugger 只读断点；不 patch 进程、不绕过 filter/renderer。当前环境没有 cdb，
  因而它不是 readiness 前置。

### T-003 — 启动隔离拓扑（RQ-001）

- Release build 当前 accepted baseline + 本 Spec 增量。用环境覆盖启动 task-owned Host：后端 Channel
  `17001`、Game `20010/20011`，Launcher API `18080`，对客户端仍广告 `7001/10010/10011`，PVF 与
  既有测试账号数据库固定到已核验路径。Host 不直接打开主数据文件：在无 Host/WAL writer 时把它复制到
  仓库外唯一 runtime 临时目录，并把 `Database__Path` 指向该隔离副本；主数据库前后 hash 必须相同。
- 启动 task-owned proxy 映射标准端口到后端端口；Host/proxy 以明确 PID receipt、hidden window 和仓库外
  raw stdout/stderr 运行。只把无凭据的 allowlist 摘录纳入仓库。
- 在请求用户动作前，HTTP bootstrap、三个前后端 listener、进程身份、proxy self-test 和 observer probe
  watcher 必须全部 ready；记录日志零点与停止命令。

### T-004 — 一次用户原生操作序列（RQ-002、RQ-003）

- 只发出一次请求：客户端 1 作为 leader、客户端 2 作为 observer，登录不同账号并进入同一 Game listener
  的共享 `[normal]` area；确认二者互相可见且不在同一队。
- leader 用原生 UI 创建带可辨识标题/预设的 Normal 队；observer 打开/刷新 ID `0x11`，记录新增行。
- leader 更改一个可见 setting/title；observer 关闭重开或原生刷新，记录同 party 行更新且没有重复。
- leader 解散队伍；observer 再刷新，记录行删除。全程成功后两端正常退出；任一步不符或 crash 时立即停止
  后续动作、保留两个进程和失败页面并回报失败步骤，供无 UI 自动化的只读诊断。

### T-005 — 同一时间线归因（RQ-002、RQ-003、RQ-004）

- 从 C2S `0x0004/0x000C/0x000D`、Host 接受日志与 S2C `0x0002/0x0009` 解出 observer/leader exact
  session、partyId、settings 和 clear，证明 `0x0002 -> 0x0009` 以及 mutation/clear 顺序。
- 将 probe 的 ID `0x11` instance/vector 变化与用户报告对齐：add 后含唯一 partyId，update 后仍唯一，
  delete 后为空；user UI 的 leader/title/count 只能授予实际观察范围的 `ClientVerified`。
- 复用已接受自动化证明不同 scope/replacement/member 分区，结合当前 static renderer 证明满员 snapshot
  不被 flush 排除、仅加入态置灰；未实际运行的附加 UI 场景继续标 `Confirmed static/High`。

### T-006 — 条件式聚焦修复（RQ-005）

- 若失败，按 Host → wire → CNUser → CParty association → ID `0x11` flush/vector → renderer/UI 顺序定位
  最早断点；保留失败 evidence，不盲目重发或更换协议猜测。
- 只有已证原因落在当前 projection/order/exact-session 或观测工具内时才修复。产品行为修复先写 faithful RED，
  再跑目标 GREEN、party/town/cleanup 回归并重新执行完整 T-003/T-004；需要新协议、ID `0x14`、快速匹配、
  客户端逻辑绕过或额外外部授权时停止并交父控制器。

### T-007 — postcheck 与恢复（RQ-001、RQ-005）

- 成功或停止后仅按 PID receipt 终止本轮 Host/proxy/probe；不结束用户或非本轮进程。若用户按成功脚本正常
  退出，确认两个 DNF 进程已经自行结束。
- 本计划默认不改两份 ClientHack.ini/launcher/插件；若诊断中确需可逆配置变更，先备份 exact 文件并在
  postcheck 恢复、比对 hash。确认任务端口释放，仓库外 raw 日志按证据策略保留或清理。
- postcheck 明确选择 retain/recover/stop；恢复成功不等于 runtime acceptance 成功。

### T-008 — 协议、审查与完成验证（RQ-004、RQ-006）

- 写 runtime evidence manifest/trace，更新 `NOTI-0x0002/0x0006/0x0009` 中实际覆盖的状态和 catalog；
  不重写历史冲突报告，不升级未观察分支。
- 运行 helper/probe 测试、受影响 party/town/cleanup 回归、Release build、显式 PVF 的全量串行 TRX 逐项
  统计、Application 分层、零新增目录 `0x023C` 和 staged/unstaged `diff --check`。
- 交独立 reviewer 按 mixed code/runtime deliverable 复核；闭合 finding 后重新执行 fresh completion
  verification。只有 AC-001..006 均有当前证据时才形成 passed result。

## Stop and recovery rules

- preflight 缺 client/account/tool/port/prior-state 任一必需条件：不启动受控动作、不请求用户操作。
- unexpected client crash、身份无法关联、probe 写权限请求或日志可能含凭据：立即停止后续动作并保留最小
  诊断证据。
- 只回收记录在本轮 PID receipt 中的 Host/proxy/probe；不以进程名批量终止，不修改只读基准客户端目录。
- 任何超出当前 TaskContract 的修复都返回父控制器，不能用合成帧、截图猜测或自动化结果替代原生 UI。

## Coverage

| Work | Requirements | Primary evidence |
|---|---|---|
| T-001..T-003 | RQ-001、RQ-005 | preflight、hash/port/process receipts、helper self-tests、readiness endpoints |
| T-004..T-005 | RQ-002..RQ-004 | exact-session frames、Host allowlist、client vector log、用户原生 UI 结果 |
| T-006..T-007 | RQ-001、RQ-005 | failure frontier、RED/GREEN 或 stop evidence、postcheck/recovery |
| T-008 | RQ-004、RQ-006 | protocol trace、automated suite、review、completion verification |
