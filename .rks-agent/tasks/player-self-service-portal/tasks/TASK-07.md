# TASK-07 — 集成验证与用户测试准备

## Task Unit Contract

- Revision: 1（标识本契约及其详细计划）
- Outcome and references: 交付 Plan T-007。覆盖 RQ-051、RQ-060、RQ-061；验收 AC-041、AC-051、AC-060、AC-061。
  - 分工：
    - 本单元（worker）负责第 1–6 节：全量自动验证、DailyRefill 偶发超时、发布构建、两份库副本、两个启动脚本、编号用户测试流程文档。
    - 主控负责：启动常驻测试服、AC-041 浏览器实测（电脑宽度与 390px、已关闭页、审计页、截图）、AC-011 在线收信的实机部分、向用户交付流程并收集反馈、汇总 `verification/completion-evidence.md`。
    - 本单元只为这些工作备好前提，不声称 AC-041 或实机验收通过。
  - 同时承接前序单元留给 T-007 的事项：
    - plan-review R-4：流程文档写明玩家发出的装备固定为最上级品质，以及原编号重放返回原结果、不受随后关闭的子开关影响。
    - plan-review R-5：在线收信若为"待用户验证"，如实标注。本单元在流程文档中把它列为用户步骤；`completion-evidence.md` 由主控按 R-5 记录。
    - TASK-05 审查：流程文档提醒管理员按需配置限额。玩家可发的堆叠物数量只受 GM 邮件既有规则约束。
    - TASK-06 审查：处理 `DailyRefill.test.tsx` 偶发超时，只改测试。
    - TASK-06 留给 T-007 的其余事项（390px 布局、物品悬停提示、角色只有基础职业名）：在流程文档中列为观察项。
- Owned boundary:
  - `Web/gm-admin/src/DailyRefill.test.tsx`：只改该文件内的超时设置。不改业务代码，不改 `vitest.config.ts` 与 `test-setup.ts`；第 2 节说明原因。
  - `Document/tasks/player-self-service-portal/用户测试流程.md`：新建，LF 行尾。
  - `artifacts/player-portal-329/`（已被 `.gitignore` 忽略，不提交）：
    - 发布产物 `host/`；
    - 库副本 `db/agent/`、`db/user/`；
    - 取副本与准备脚本 `tools/`；
    - `run-agent-server.sh`、`run-user-server.sh`；
    - agent 管理员与测试账号凭据 `agent-credentials.env`；
    - 日志 `logs/`。
  - 本任务文件末尾追加 `## Result`；阻塞时追加 `## Blocker`。
  - 不触碰的东西：
    - 主干工作区 `E:\Project\DNF\DNF-JP-86-Server` 下的任何文件，主干库只经 SQLite backup API 只读打开；
    - 用户客户端目录的配置；
    - 41xxx、42xxx 端口与其进程（55872、55620）；
    - 任何已有的 Node / dotnet 进程。
- Required checks:
  - AC-051：
    - `dotnet build Server/86JP.Server.slnx` 零警告零错误；
    - 全量 `dotnet test Server/86JP.Server.slnx`，设置 `PVF_ARCHIVE_PATH`；
    - `Web/gm-admin`：`npm test` 全量至少 3 轮，`npm run build` 通过；
    - 失败、跳过逐项给出身份与原因（BuildAndTest「失败归因」）。
  - DailyRefill：改后全量 `npm test` 的轮次中，该用例全部通过。
  - 发布：`host/wwwroot/gm/index.html` 存在且与 `Web/gm-admin/dist/index.html` 一致。agent 副本冒烟中 `/gm/` 返回 200，同时检查 `localhost` 与 `127.0.0.1` 两个主机名。
  - AC-060：
    - 记录原库路径、两份副本路径、迁移备份文件；
    - 两份副本迁移后 `PRAGMA user_version = 7`，`PRAGMA integrity_check = ok`；
    - 账号数、角色数与取副本当时的原库一致；
    - 用户副本在交付前未被任何 Host 打开写过（文件哈希在迁移后与交付时一致）；
    - 主干服务在准备前后仍未运行，原库三个文件（主文件、`-wal`、`-shm`）的大小与修改时间不变。
  - 脚本：两个脚本中 `bash -n` 通过；端口全部在 43xxx；`Database__Path` 分别指向各自副本；用户脚本不含 Bootstrap 配置；gate token 不出现在任何输出与文档中。
  - AC-061：流程文档逐项覆盖 spec AC-061 列出的步骤，每步写明预期。
- Dependencies: TASK-01、TASK-02、TASK-03、TASK-04、TASK-05、TASK-06（均已提交）。
- Result review: required。本单元的错误不会在自动检查里显形：
  - 用户副本取错（空库、历史备份、被 agent 写过）会让用户在错误的数据上验收；
  - 脚本端口或库路径写错可能撞上其它会话的测试服，甚至打开原库；
  - 流程文档漏步或写错预期，会让用户验收不到 AC-061 要求的行为；
  - 计数与哈希只能挡住一部分，文档与脚本的正确性要靠审查判断。

## Detailed Plan

### 0. 执行约束与顺序

- 顺序：
  1. 第 1 节（全量自动验证）与第 2 节（DailyRefill）先做，两节都通过后再做后续各节；
  2. 第 3 节（发布）；
  3. 第 4 节（库副本）；
  4. 第 5 节（脚本与 agent 冒烟）；
  5. 第 6 节（流程文档，要用到前几节的实际路径与哈希）。
- 长命令（`dotnet test`、`npm test` 三轮、`dotnet publish`）后台运行，完整输出写到 `artifacts/player-portal-329/logs/`。
- 不用 `git checkout` / `restore` / `stash`，不提交。新文件 LF。
- 进程纪律：
  - 冒烟只启动 agent 副本的 Host 一次，记下 PID、启动时间与可执行路径；
  - 结束时三项核对一致才结束该 PID；
  - 不按名称结束进程，不碰任何其它 `dotnet` / `node` 进程。

### 1. 全量自动验证（AC-051）

1. 构建：
   ```
   dotnet build Server/86JP.Server.slnx 2>&1 | tee artifacts/player-portal-329/logs/build.log
   ```
   结尾必须是 `0 个警告 / 0 个错误`（或英文同义）。
2. 全量测试：
   ```
   PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
     dotnet test Server/86JP.Server.slnx --logger 'console;verbosity=detailed' \
     > artifacts/player-portal-329/logs/dotnet-test.log 2>&1
   ```
   - 记录退出码，以及各测试项目的通过、失败、跳过数。
   - 记录 PVF 的 SHA-256 前八位。BuildAndTest 写明 11-11 的合并档案前缀为 `A443F87B`；若不一致，跳过或失败的归因要据此调整。
   - 失败时按 BuildAndTest「失败归因」取得完整身份（异常、栈根、用例名），再隔离复跑。已知模式三样对上才归入；对不上的不复跑了事，记入结果。
   - 跳过的用例逐个列出其跳过原因（来自日志的 skip reason）。
3. 前端：
   - `cd Web/gm-admin`，`node_modules` 已存在时先不 `npm ci`；第 3 节发布时再 `npm ci`。
   - 第 2 节完成后连跑 `npm test` 3 轮，每轮记录文件数、用例数、失败项。
   - `npm run build` 通过。现有 chunk > 500 kB 警告照实记录，不处理。

### 2. DailyRefill 偶发超时（只改测试）

- 现象：TASK-06 两轮审查中，全量并行时用例"新增通过补给专用目录选择物品并保存数量、方式及原版本"有一轮在 3353 ms 失败。
  - 该用例有十余个连续的 `findBy*` / `waitFor` 步骤，每步受 `test-setup.ts` 的全局 `asyncUtilTimeout: 3000` 约束。
  - 单跑 0.27–0.92 s；基线副本全量运行也出现 1.82 s。
  - 结论：并行负载下的抖动，不是行为回归。
- 先取证：读审查日志确认是哪一级超时。
  - 若是某一步 `findBy` / `waitFor` 超过 3000 ms：放宽该用例的每步等待。
  - 若是 vitest 的单用例 `testTimeout`（默认 5000 ms）：放宽单用例上限。
  - 日志没有留下的，本地用 `--reporter=verbose` 跑全量 5 轮，看能否复现并取得报错文字。
- 改法：只动这一个用例。
  - 在 `DailyRefill.test.tsx` 的该 `it` 加单用例超时（如 `20_000`）。
  - 在该用例内，把从打开选择器到保存成功这几步的 `findBy*` / `waitFor` 显式传 `{ timeout: 10_000 }`。
  - 不改全局 `asyncUtilTimeout`，也不改 `maxWorkers`：全局改动会掩盖其它用例的真实变慢，且影响 CI 的整体时长。
  - 放宽等待不改变断言内容：用例失败时仍会报出同一断言，只是等得更久。
- 依据写入结果：原超时级别、改动行、改后全量轮次中该用例的耗时范围。

### 3. 发布构建与 `/gm/` 静态页

1. `cd Web/gm-admin && npm ci && npm run build`，确认 `dist/index.html` 与 `dist/assets/` 生成。
2. 发布 Host：
   ```
   dotnet publish Server/src/Dfo.Server.Host -c Release -o artifacts/player-portal-329/host
   ```
   - `Dfo.Server.Host.csproj` 把 `Web/gm-admin/dist/**` 作为 Content 链接到 `wwwroot/gm/`（`Document/gm-web-admin.md`「构建与启动」）。
   - 用独立的发布目录，后续 `dotnet build` / `dotnet test` 不会覆盖测试服运行的产物，提交与产物对应关系固定。
3. 核对：
   - `host/wwwroot/gm/index.html` 与 `dist/index.html` 字节一致；
   - `host/wwwroot/gm/assets/` 文件清单与 `dist/assets/` 一致；
   - `host/appsettings.json` 中 `GmWeb.Enabled` 默认为 false（脚本用环境变量打开）；
   - 记录 `git rev-parse HEAD` 与工作区是否有未提交改动（DailyRefill 改动属于测试，不进产物）。
4. 图标包：
   - `GmWeb:IconPack` 默认 `Data/gm-icons.zip`，相对 content root。
   - 若发布目录没有它，脚本用 `GmWeb__IconPack` 只读指向工作区里最新的 `gm-icons.zip`（已知 `E:\Project\DNF\Document\onekey-package-0.2.7-20261021\gm-icons.zip`，执行时再查有无更新的）。
   - 找不到时图标显示降级，在流程文档里注明。

### 4. 两份库副本（AC-060）

1. 核实主干库：
   - 确认主干服务未运行：7001、18080、18081 无监听，且没有 `Dfo.Server.Host` 进程的路径指向主干 bin。
   - 主干库为 `E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db`（主控已核实，是此前各功能测试使用的主干库来源）。
   - 只读核对主干 Release 目录 `appsettings*.json` 的 `Database:Path`，确认该路径就是配置值，不是猜测。
   - 记录三个文件（主文件、`-wal`、`-shm`）的大小、修改时间与 SHA-256。
2. 取副本：在 `artifacts/player-portal-329/tools/` 写 Python 脚本 `copy_db.py <src> <dst>`。
   - 用 `sqlite3.connect('file:<src>?mode=ro', uri=True)` 只读打开，`src.backup(dst)` 写入目标。
   - 不对原库执行任何写语句或 `PRAGMA`，也不做 checkpoint。只读连接仍可读到 WAL 中已提交的内容。
   - 脚本运行前后各记录一次原库三个文件的大小与修改时间，两次一致。
   - 依次产出 `db/agent/dfo-server.db` 与 `db/user/dfo-server.db`，各自从原库独立 backup，不从对方复制。
   - 在副本上读出 `PRAGMA user_version`（预期 0）、`accounts` 行数、`characters` 行数，以及 `delete_flag = 0` 的角色数。
3. 迁移：对两份副本分别运行
   ```
   dotnet run --project Tool/DbMigrate -c Release -- <副本路径>
   ```
   - 带默认备份：工具在副本旁生成 `dfo-server.db.v0-<UTC>.bak`，记录文件名。
   - 迁移后核对 `user_version = 7`、`integrity_check = ok`，账号数与角色数与迁移前相同。
   - `player_portal_settings` 无行，即玩家后台默认关闭。
   - `gm_audit` 的 `actor_kind` 全为 `gm`。
4. agent 副本额外准备（只动 agent 副本）：
   - 管理员：建旁表 `agent_saved_gm_administrators`，把 `gm_administrators` 的行全部复制过去，再清空 `gm_administrators`，使 Bootstrap 生效。管理员 ID 由 AUTOINCREMENT 续号，不与审计里的旧 ID 重复。用户副本的管理员原样保留。
   - 凭据：生成 agent 管理员的登录名与随机密码，写入 `artifacts/player-portal-329/agent-credentials.env`，供脚本与主控读取，不在输出里打印。
   - 玩家测试账号：主控实测需要一个"有角色、密码可知"的玩家账号。冒烟时经 gate `/register` 注册一个 agent 测试账号，得到合法的密码记录。停服后，在 agent 副本里选一个有至少两个未删除角色、当前无在线标记的现有账号，把注册账号的密码记录复制到该账号的 `password_hash`。
     - 原值存入旁表 `agent_saved_account_passwords`，账号名与密码写进 `agent-credentials.env`。
     - 新注册的空账号保留，作为"无角色账号"。
     - 这一步只改 agent 副本。若注册接口或密码记录格式不允许这样做，改为在结果中报告，由主控决定。
5. 用户副本封存：迁移后记录 `db/user/` 下全部文件的 SHA-256。本单元不再打开它（之后只读核对计数也用 `mode=ro`），交付时复核哈希一致。

### 5. 启动脚本与 agent 冒烟

两个脚本放在 `artifacts/player-portal-329/`，均为 bash、LF，写法参照 auction-324 的 `run-test-server.sh`：

- 共同部分：
  - `cd` 到 `artifacts/player-portal-329/host`，`exec dotnet Dfo.Server.Host.dll`。
  - PVF：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
  - 网络端口：
    - `Networking__Enabled=true`、`Networking__ChannelPort=43001`；
    - `Networking__GamePorts__0..2=43010/43011/43100`；
    - `Channel__UseScriptCatalog=false`，`Channel__Channels__0..2__Port` 同游戏端口。
  - LauncherAuth：
    - `LauncherAuth__Enabled=true`、`LauncherAuth__ListenUrl=http://127.0.0.1:43080`、`LauncherAuth__PublicChannel__Port=43001`；
    - `LauncherAuth__GateToken` 运行时从 `E:/Project/DNF/DNFClient-A21-protocol/Launcher.ini` 读取（`sed` + `tr -d '\r'`），不写死、不回显。
  - GmWeb：
    - `GmWeb__Enabled=true`、`GmWeb__ListenUrl=http://127.0.0.1:43081`；
    - `GmWeb__IconPack` 见第 3 节。
  - 开头检查 43001、43010、43011、43100、43080、43081 是否被占用，占用则报错退出。这也保证两个脚本不会同时运行。
- `run-agent-server.sh`：
  - `Database__Path` 指向 `db/agent/dfo-server.db`；
  - 从 `agent-credentials.env` 读出 `GmWeb__Bootstrap__LoginName`、`GmWeb__Bootstrap__Password`，不回显；
  - 日志写 `logs/agent-host.log`。
- `run-user-server.sh`：
  - `Database__Path` 指向 `db/user/dfo-server.db`；
  - 不设任何 Bootstrap 变量，使用用户原有管理员；
  - 日志由主控决定。
- 静态检查：
  - 两个脚本 `bash -n` 通过；
  - `grep` 确认端口只有 43xxx、两份库路径各自正确、用户脚本无 `Bootstrap`。
- agent 冒烟（短暂，只针对 agent 副本）：
  1. 后台运行 `run-agent-server.sh`，记录 PID 并核对其路径与启动时间。
  2. 等待日志出现 Host 启动完成，确认 GmWeb、LauncherAuth、Channel、Game 端口已监听。
  3. 记录 UDP 探测端口的绑定情况：其它会话的测试服可能已占用固定 UDP 端口。若因此启动失败，记录现象，由主控决定是否在脚本中设 `Networking__UdpDiscoveryEnabled=false`；本单元不擅自关闭。
  4. 检查以下 URL：
     - `curl -s -o /dev/null -w '%{http_code}'` 访问 `http://127.0.0.1:43081/gm/` 与 `http://localhost:43081/gm/`，均为 200，且正文含构建产物的入口脚本名；
     - `GET /api/player/v1/auth/status` 返回 200，并显示玩家后台关闭；
     - `GET /api/gm/v1/auth/status` 返回 200。
  5. 用 Bootstrap 管理员登录一次 GM（取 csrf → 登录），确认管理员可用；再经 gate `/register` 注册第 4.4 节的测试账号。不改玩家后台开关，开关的操作留给主控实测。
  6. 结束：PID、可执行路径、启动时间三项核对一致后，只结束该 PID。确认 43xxx 端口释放，再做第 4.4 节的密码记录复制。
- 用户副本不做冒烟。AC-060 的"可连接"用 `mode=ro` 打开并读出版本与计数来证明；Host 实际打开由主控启动用户测试服时完成。

### 6. 编号用户测试流程文档（AC-061）

新建 `Document/tasks/player-self-service-portal/用户测试流程.md`，中文，称呼用"你"。

**文档头**：

- 分支 `task/player-self-service-portal-329` 与提交号；发布目录与构建时间。
- 原库路径、用户副本路径、迁移备份文件、副本迁移前后的账号数与角色数。
- 启动方式：`bash artifacts/player-portal-329/run-user-server.sh`，由主控启动。与 agent 测试服不同时运行。
- 端口表：Channel 43001，Game 43010/43011/43100，LauncherAuth 43080，GmWeb 43081。
- 后台地址：
  - 用 `http://localhost:43081/gm/`；
  - 主干后台用的是 `127.0.0.1`，同名 Cookie 按主机名区分，两者不会互相覆盖。
- 客户端：
  - 用 `DNFClient-A21-protocol`。测前把 `Launcher.ini` 的 `BaseUrl` 从原值临时改成 `http://127.0.0.1:43080/`，Token 不动；
  - 测后改回原值。原值写在文档里，供你核对；
  - 修改由你自己操作。
- 就绪情况一栏留给主控填写。

**前置条件**：

- 管理员用你原有的 GM 账号。
- 玩家账号 A 至少有两个角色，其中一个保持离线、一个用客户端登录在线。
- 可选：账号 B，用于确认 A 看不到 B 的数据。
- 记下 A 测前的点券与代币券余额，以及两个角色的邮箱数量。

**编号步骤**（每步写操作与预期）：

1. 管理员登录后台 →「玩家后台」页：总开关为关、两个子开关为开、限额全为 0。退出管理员后，登录页不显示"玩家"选项。
2. 管理员打开总开关 → 登录页出现"玩家 / 管理员"切换，默认玩家。另外提示：限额默认不限，正式开放前请按需设置（例如点券每日上限）。
3. 玩家用账号 A 的游戏账号密码登录：
   - 进入玩家界面，只看到 A 的余额与未删除角色；
   - 错误密码与不存在的账号提示相同。
4. 加点券 1000 → 预览显示加后余额；提交后余额 +1000，代币券不变。
5. 加代币券 → 代币券增加；在线角色在游戏内收到余额提醒，商城余额一致。
6. 限额（可选）：管理员设点券单次上限 1000 → 玩家加 1001 被拒、余额不变；设每日上限后，"今日已用"显示正确。测后把限额改回原值或 0。
7. 给离线角色发信（物品 + 数量 + 金币 + 标题正文）：
   - 角色上线后在邮箱收到，附件与金币正确；
   - 注明 R-4：玩家发出的装备一律为固定最上级品质，没有强化等高级选项。
8. 给在线角色发信 → 游戏内出现新邮件提醒，可直接收取。这一项是 AC-011 的在线实机部分，以你的结果为准。
9. 记录页：上述加点券与发信按时间倒序列出，可按类型筛选；不含账号 B 的操作。
10. 管理员关闭点券子开关：
    - 加点券表单不可提交；发信与查看仍可用。
    - 注明重放：此前已成功的请求用原编号重试时，仍返回原结果，不会重复入账，也不受刚关闭的开关影响。这是设计行为；页面上只在"结果未确认后用原编号重试"时可能遇到。
    - 重新打开点券子开关。邮件子开关同样验证一次。
11. 管理员关闭总开关 → 已登录玩家下一次操作进入"玩家后台已关闭"页；会话列表清空；登录页不再显示"玩家"。
12. 重新打开总开关 → 玩家可重新登录。管理员在会话列表"踢下线"A → A 的下一次操作回到登录页，其他玩家不受影响；"全部踢下线"同理。
13. 审计页：
    - 按"玩家自助"筛选，列出玩家登录（成功与失败）、加点券、发信；
    - 操作者显示登录名、"玩家"标记与账号 ID；
    - 管理员修改设置、踢会话在审计中可见；
    - 迁移前的旧审计仍显示为管理员。
14. 持久化：
    - 在游戏内正常退出 A 的角色并重新登录：余额与第 4–5 步一致，已收与未收的邮件都在；
    - 停止测试服再启动后，余额与邮件仍在；
    - 玩家后台的开关与限额保持最后一次保存的值。
15. GM 回归：用管理员原有的「发点券」给 A 发一笔、「写邮件」给角色发一封，结果与以往一致，审计显示为管理员。
16. 收尾：
    - 停止测试服；
    - 把 `Launcher.ini` 的 `BaseUrl` 改回原值；
    - 主干库未被改动（副本独立）。

**观察项**：

- 390px 宽度（手机或浏览器窄窗）下用底部标签完成第 3–9 步。
- 物品悬停提示可见。
- 角色只显示基础职业名，不含转职名（已知限制）。

**结果回报格式**：每步填"通过 / 不通过 + 现象"。

### 7. 结果记录

`## Result` 按第 1–6 节列出：命令、退出码、计数、日志路径、哈希、PID 核对、未覆盖项。AC-041 与在线实机收信明确写为"由主控 / 用户执行，本单元未验证"。

## Result

### 执行概况

- 基于提交 `d6cd73cbf`（`task/player-self-service-portal-329`）执行，执行时间 2026-11-14 03:00–03:25（北京时间）。
- 未提交的改动：
  - `Web/gm-admin/src/DailyRefill.test.tsx`（只改测试）；
  - 新建 `Document/tasks/player-self-service-portal/用户测试流程.md`；
  - 本文件。
- 上述三个文件 CR 均为 0。没有 commit，没有使用 checkout / restore / stash。
- 测试服材料都在 `artifacts/player-portal-329/`（被 `.gitignore` 忽略）。日志在其 `logs/` 下。
- 协调方裁定：
  - 4.4：照做；
  - agent 脚本关闭 UDP 探测：已设 `Networking__UdpDiscoveryEnabled=false`；
  - 用户脚本保持默认开启；流程文档"前置条件"已写入 UDP 端口占用的处理方法。

### 1. 全量自动验证（AC-051）

- **构建**：`dotnet build Server/86JP.Server.slnx`，0 个警告、0 个错误（`logs/build.log`）。
- **全量服务端测试**：
  - 命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`（SHA-256 前缀 `a443f87b`，与 BuildAndTest 记录的合并档案一致），`dotnet test Server/86JP.Server.slnx --no-build`，完整输出在 `logs/dotnet-test.log`。
  - 结果：9 个测试项目共 11424 例，1 失败、3 跳过，退出码 1。

  | 项目 | 总数 | 通过 | 失败 | 跳过 |
  |---|---|---|---|---|
  | Dfo.Server.Application.Tests | 6805 | 6804 | 1 | 0 |
  | Dfo.Server.Host.Tests | 322 | 321 | 0 | 1 |
  | Dfo.Server.Infrastructure.Tests | 1040 | 1038 | 0 | 2 |
  | 其余 6 个项目（Pvf、HttpApi、Networking、Persistence、Protocol、DbMigrate） | 3257 | 3257 | 0 | 0 |

- **唯一的失败**：`MirrorDungeonEntryTests.Refused_mirror_entry_does_not_create_a_run_or_reserve_daily_entry(boundary: "quota")`。
  - 现象：`Assert.Equal() Failure: Expected DailyEntryLimitReached, Actual null`，见 `MirrorDungeonEntryTests.cs:93`。
  - 隔离复跑：`MirrorDungeonEntryTests` 单独连跑 5 次，每次都只有该例失败，是稳定失败（`logs/mirror-rerun.log`）。
  - 归因：这是用例本身依赖时刻，与本任务无关。
    - 用例在 `MirrorDungeonEntryTests.cs:206` 用北京时间的**自然日**插入 `character_mirror_daily`（`DateTimeOffset.UtcNow.ToOffset(+8)` 的日期）。
    - 被测服务按 `IDailyResetRuleService` 的**游戏日**查用量，日切 `Game:DailyResetTime = 06:00`。
    - 每天北京时间 00:00–06:00，两个日期相差一天，预置的额度行查不到，入场不被拒，用例就失败。本次运行在 03:06–03:12，正落在这个窗口。
    - 本分支相对基线 `20bcaf37c` 没有改动 `World/`、该测试、日切规则（只新增了 `PlayerPortalDailyUsage.cs`，它不在该路径上）。引入这种写法的 `d079dd449` 在主干上。
  - 未在本任务修复（不在本单元边界内）。建议另开 issue：把该用例的日期改用 `IDailyResetRuleService.PeriodDay`。
  - 北京时间 06:00 之后复跑应当通过，本单元未等到该时刻复跑。
- **3 个跳过**，都是环境决定的预期跳过：
  - `HostShutdownOrderTests.Sigterm_with_an_open_game_connection_…`：`SIGTERM is not available on Windows.`
  - `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog` 与 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`：`An archive is reachable from this working tree.`。设置了 PVF 后，"缺档案"用例按设计跳过。
- **前端**（`Web/gm-admin`）：
  - `npm ci` 通过，报告 2 个 high 级依赖漏洞（`npm audit`），本单元未处理。
  - `npm run build` 通过，只有既有的 chunk > 500 kB 警告。
  - 第 2 节的改动之后，全量 `vitest run --reporter=verbose`（即 `npm test` 的脚本）连跑 5 轮，每轮 16 个文件、192 例全过（`logs/npm-test-after-1..5.log`）。

### 2. DailyRefill 偶发超时

- **取证**：
  - TASK-06 审查记录中，失败用例的总耗时是 3353 ms。它小于 vitest 单用例上限 5000 ms，大于 `test-setup.ts` 的全局 `asyncUtilTimeout: 3000`，可知是某一步 `findBy*` / `waitFor` 的等待超时。
  - 本次改动前全量跑了 5 轮，未复现，该用例耗时 863–1099 ms（`logs/npm-test-before-1..5.log`）。
- **改法**：只改这一个用例。
  - 定义局部常量 `slow = { timeout: 10_000 }`，传给它的 5 个 `findBy*` 与 3 个 `waitFor`；
  - 单用例上限设为 `30_000`；
  - 断言与操作不变，加了两行注释说明原因。
  - 不改全局 `asyncUtilTimeout` 与 vitest 并发设置，避免掩盖其它用例真实变慢。
- **改后**：5 轮全量中，该用例耗时 853–1093 ms，全部通过。
- **变异核对**：把 `'选择物品 7'` 改成 `'选择物品 8'`，该用例在 10211 ms 失败，报 `Unable to find role="button" and name "选择物品 8"`。这说明放宽等待没有吞掉失败。恢复后文件 SHA-256 与改动后一致（`41c3ed31…`）。

### 3. 发布构建

- `npm ci && npm run build` 生成 `dist/`，随后执行 `dotnet publish Server/src/Dfo.Server.Host -c Release -o artifacts/player-portal-329/host`，退出码 0（`logs/publish.log`）。
- `host/wwwroot/gm/index.html` 与 `dist/index.html` 逐字节一致，`assets/` 文件清单一致，入口 `assets/index-BxbXS_V_.js`。
- `host/appsettings.json` 中 GmWeb 仍为 `Enabled: false`，由脚本用环境变量打开。
- 图标包：发布目录没有图标包，两个脚本都用 `GmWeb__IconPack` 只读指向 `E:/Project/DNF/Document/onekey-package-0.2.7-20261021/gm-icons.zip`。工作区内没有更新的图标包。

### 4. 两份库副本（AC-060）

- **主干**：未运行。7001、18080、18081 无监听，也没有指向主干 bin 的 Host 进程。
- **原库**：`E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db`。
  - 主干 Release 的 `appsettings.json` 中 `Database:Path = Data/dfo-server.db`，content root 为该目录。
  - 取副本前后核对了三个文件：

  | 文件 | 大小 | SHA-256 前缀 | 前后变化 |
  |---|---|---|---|
  | 主文件 | 20262912 | `89b2915a` | 无 |
  | `-wal` | 4140632 | `fd8e232c` | 无 |
  | `-shm` | — | `c8e2547f` | 内容不变；修改时间随只读连接的读锁更新，`-shm` 是 WAL 索引，不是数据 |

- **取副本**：`tools/copy_db.py` 以 `mode=ro` 只读打开原库，用 backup API 分别产出两份副本，各自独立取自原库（`logs/copy-agent.log`、`logs/copy-user.log`）。两份副本迁移前均为 `user_version 0`、integrity ok、账号 4、角色 9（未删除 8）。
- **迁移**：`dotnet run --project Tool/DbMigrate -c Release -- <副本>`（`logs/migrate-*.log`），两份都 `Migrated from schema version 0 to 7`。
  - 迁移备份：
    - agent：`db/agent/dfo-server.db.v0-20261113190809.bak`
    - 用户：`db/user/dfo-server.db.v0-20261113190817.bak`
  - 迁移后（`tools/inspect_db.py`，只读）：`user_version 7`、integrity ok；账号 4、角色 9（未删除 8），与原库一致；三张玩家后台表 0 行，即默认关闭；`gm_audit` 83 条全部为 `gm`；管理员 1 个。
- **用户副本** `artifacts/player-portal-329/db/user/dfo-server.db`：
  - 迁移后 SHA-256 前缀 `6fba1b59`，交付核对时未变。
  - `-wal` 为空文件（只读检查连接留下的），备份文件前缀 `fb729ace`。
  - 没有被任何 Host 打开过，"可连接"由只读打开并读出版本与计数证明。
- **agent 副本** `artifacts/player-portal-329/db/agent/dfo-server.db`，只改这一份：
  - 管理员：原管理员整行复制到旁表 `agent_saved_gm_administrators` 后清空 `gm_administrators`，同时清空了 `gm_sessions`（计划未写这一项，只影响 agent 副本）。Bootstrap 后新管理员 ID 为 2，全部权限（15）。
  - 玩家测试账号：
    - 冒烟时经 gate `/register` 注册了一个无角色账号（账号 ID 6）。
    - 把它的密码记录复制到账号 4，账号 4 有 4 个未删除角色和 1 个已删除角色；原值存入旁表 `agent_saved_account_passwords`。
    - 账号 3 有 3 个角色，可作账号 B。
  - 第二次冒烟时，两个账号经 gate `/login` 都返回 200。
  - 现状：账号 5，`gm_audit` 84 条（多出 Bootstrap 管理员的一次登录），integrity ok，v7。
  - 凭据在 `artifacts/player-portal-329/agent-credentials.env`（GM 登录名与密码、玩家登录名与密码、账号 ID），未在任何输出或文档中打印。

### 5. 启动脚本与冒烟

- `artifacts/player-portal-329/run-agent-server.sh` 与 `run-user-server.sh`：LF 行尾，`bash -n` 通过。
- 两个脚本共同点：
  - 只用 43001、43010、43011、43100、43080、43081，启动前检查这些端口，有任何一个在监听就退出，所以两个脚本不会同时运行；
  - 运行 `host/` 发布产物，PVF 为 `DNFClient-A21-protocol/Script.pvf`；
  - gate token 运行时从该目录 `Launcher.ini` 读取，不回显；
  - GmWeb `http://127.0.0.1:43081`。
- 两个脚本的差别：
  - agent 脚本：`Database__Path` 指向 agent 副本；Bootstrap 取自 `agent-credentials.env`，启动前 unset 凭据变量；UDP 探测关闭。
  - 用户脚本：`Database__Path` 指向用户副本；没有 `Bootstrap__` 变量；UDP 探测保持默认开启。
- **冒烟**：只针对 agent 副本，共两次，每次几十秒。
  - 第一次（PID 37668，03:18:27 启动）：
    - 6 个端口都由该 PID 监听；启动日志没有 fail / crit / exception。
    - `http://127.0.0.1:43081/gm/` 与 `http://localhost:43081/gm/` 均返回 200，且含入口脚本名；入口 JS 返回 200。
    - `/api/player/v1/auth/status` 返回 200 `{"enabled":false}`；`/api/gm/v1/auth/status` 返回 200 `{"initialized":true}`。
    - Bootstrap 管理员经 csrf → 登录返回 200，`auth/session` 返回 200。
    - gate `/register` 返回 201。
  - 第二次（PID 54140，03:19:48 启动）：只验证账号 4 与注册账号的 gate 登录，均返回 200。
  - 两次结束前都核对了三项：该 PID 是 43081 的监听者；可执行文件为 `C:\Program Files\dotnet\dotnet.exe`；已加载模块为 `artifacts\player-portal-329\host\Dfo.Server.Host.dll`，且创建时间与启动时刻相符。三项一致后，只对该 PID `Stop-Process`。
  - 结束后 43xxx 无监听，41xxx / 42xxx 测试服（55872、55620）仍在监听。
  - 两次后台任务报告的退出码 127，是进程被结束的结果。
- 没有留下任何常驻测试服。

### 6. 用户测试流程文档（AC-061）

文档 `Document/tasks/player-self-service-portal/用户测试流程.md` 包含：

- **测试服信息**：分支 / 提交、运行产物、启动脚本（由主控启动）、端口、后台地址 `http://localhost:43081/gm/` 及 Cookie 理由、原库路径、测试库路径与哈希、迁移备份、账号 / 角色计数、PVF、管理员。"就绪情况"一栏留给主控填写。
- **客户端说明**：
  - `DNFClient-A21-protocol` 与 `DNFClient-A21-protocol2` 的 `BaseUrl` 目前指向另一个测试服 41080；`DNFClient-A21-protocol3` 指向 18080。
  - 文档建议选当前没在用的目录，改前记下原值，测后改回。
- **前置条件**：包括 UDP 端口占用的处理方法、账号与角色要求、测前记录的余额与邮件数、按需配置限额的提醒。
- **16 个编号步骤**，每步写有预期，覆盖 spec AC-061 的全部项：
  - 开启玩家后台 → 玩家登录 → 加点券 / 代币券并在游戏内核对 → 限额（可选）→ 给离线与在线角色发信并在游戏内收取 → 记录页；
  - 子开关 → 总开关关闭 → 重开与踢下线 → 审计页 → 正常退出重登与重启后持久 → GM 发点券 / 发邮件回归 → 收尾。
- **R-4**：第 7 步写明玩家发出的装备固定为最上级品质。
- **重放**：第 10 步写明子开关关闭后，原编号重放仍返回原结果。
- **观察项**：390px、物品悬停提示、只有基础职业名。

### 未覆盖与交接

- **AC-041 浏览器实测**：由主控执行，本单元未验证。
- **AC-011 在线实机收信**（流程第 8 步）：由用户执行，本单元未验证。按 R-5，`completion-evidence.md` 中这一项须记为待用户验证。
- **全量测试有 1 个失败**：`MirrorDungeonEntryTests … (boundary: "quota")`，判为既有的时刻依赖缺陷，与本分支无关，归因见第 1 节。AC-051 的"全量通过"因此不能写成无条件成立，须附上这项说明，或在北京时间 06:00 后复跑确认。
- **依赖漏洞**：`npm audit` 报 2 个 high 级依赖漏洞，本单元未处理。

## Result Review

### Independent Quick Review R1

- 审查对象：契约 Revision 1 与详细计划；执行轮次为上文 `## Result`（基于提交 `d6cd73cbf`）；未提交改动 `Web/gm-admin/src/DailyRefill.test.tsx`、`Document/tasks/player-self-service-portal/用户测试流程.md`，以及被忽略的 `artifacts/player-portal-329/`（两个脚本、`tools/`、`db/`、`host/`、`logs/`）。
- 适用标准：reviewing-work 的 Task Unit、Code、Deliverable，限本单元范围。上位依据为 plan T-007、spec RQ-051/060/061 与 AC-051/060/061、plan-review R-4、`Document/FeatureDevelopment.md` §4。
- 审查者独立于计划与实现；只读核对，没有启停任何进程，没有读取 `agent-credentials.env`，也没有打开 agent 副本。

```yaml
kind: rks-review
verdict: changes_required
```

#### 核对结果

1. **库副本（AC-060）：成立。**
   - 主干 Release `appsettings.json` 的 `Database:Path = Data/dfo-server.db`。
   - 原库主文件与 `-wal` 的 SHA-256 前缀仍为 `89b2915a`、`fd8e232c`，大小和修改时间仍是 10-30。`-shm` 只有修改时间变化，内容哈希不变（`copy_db.py` 只比较它的内容，结果已如实报告）。
   - `copy_db.py` 以 `mode=ro` 打开原库后调用 backup API，两份副本各自从原库独立取得。
   - 迁移备份比原库小，原因是 DbMigrate 用 `VACUUM INTO` 生成备份，不是取错了文件。
   - 用户副本用 `immutable=1` 只读复核：`user_version 7`、integrity ok；账号 4、角色 9（未删除 8）；管理员 1 个；三张玩家后台表存在；`gm_audit` 83 条全部为 `gm`；主文件 SHA-256 前缀仍为 `6fba1b59`，`-wal` 为 0 字节。
2. **脚本：成立。**
   - TCP 端口只有 43001、43010、43011、43100、43080、43081。启动前的监听检查同时覆盖 IPv4 和 `[::]`。
   - `Database__Path` 分别指向 `db/agent` 与 `db/user`，两个脚本都不会打开原库。用户脚本没有 `Bootstrap`。
   - gate token 运行时从 `Launcher.ini` 读取，不回显。protocol、protocol2、protocol3 三个目录的 Token 哈希一致，用哪个客户端目录都能用。
   - `bash -n` 通过，两个脚本都是 LF。
3. **DailyRefill：成立。** 只改了一个用例：8 处等待传入 `{timeout:10_000}`，单用例上限设为 30 s，断言不变。变异核对证明真实失败仍会报出。全局设置未动。
4. **MirrorDungeon 失败的归因：成立。**
   - 本分支相对合并基 `20bcaf37c` 的 diff 没有触及 `World/`、Mirror、日切相关文件。
   - 测试第 206 行用北京时间自然日写入 `character_mirror_daily`；被测服务第 30 行用 `daily.PeriodDay(UtcNow)`，默认日切为 06:00（`ServerConfigService`），06:00 之前 `PeriodDay` 仍是前一天。
   - 03:xx 连续 5 次稳定失败，与这个机制吻合，属于主干既有的时刻依赖缺陷。
   - AC-051 须附这条说明，或在 06:00 后复跑确认，Result 已写明。
5. **流程文档（AC-061）**：覆盖 AC-061 的全部步骤，R-4、重放说明、限额提醒、UDP 说明、BaseUrl 改动与还原都在，没有写入凭据值。逐步预期对照代码核实如下：
   - 开关点击即提交；
   - 限额名称为「点券单次」「点券每日」，判定为 `used+amount > perDay`，所以第 6 步"500 成功、再加 1 被拒"正确；
   - 重放先于子开关判定（`PlayerPortalCeraDomainService`）；
   - 邮件 30 天有效；
   - 导航名「发放 → 发点券 / 写邮件」「操作记录」「账号与角色」与界面一致。
   除下面 F-1 外没有发现预期写错。

#### 阻塞项

- **F-1：第 11 步"（或刷新）"的预期是错的。**
  - 依据：`Portal.tsx` 的 `probe()` 在刷新时先读 `auth/status`。总开关关闭后它返回 `enabled=false`，页面进入 `entry` 状态，显示的是没有"玩家"选项的管理员登录页，不是"玩家后台已关闭"页。只有已登录的玩家页面上发出的请求收到 `portal_closed` 时，才会进入已关闭页（RQ-042）。
  - 后果：用户按文档刷新，会把正确行为报成"不通过"，干扰 AC-061 的结论。
  - 最小修正：删掉"（或刷新）"，改为"在玩家页面上切换一次页面或提交一次操作"。也可以补一句：刷新后显示没有"玩家"选项的登录页，这同样正确。

#### 非阻塞提醒

- **N-1：UDP 端口当前被占用，用户测试服现在起不来。** 本次审查时，UDP 12311/12312 被 PID 55872（41xxx 测试服，属于其它会话）占用。`run-user-server.sh` 保持 UDP 探测开启（协调方的裁定），`A21UdpDiscoveryService.StartAsync` 绑定失败会让 Host 启动失败。启动用户测试服之前，主控须先解决：请那个会话停服，或者征得用户同意后在用户脚本里关闭 UDP 探测。
- **N-2：第 13 步预期措辞可再精确。** 用不存在的账号登录失败时，审计按 RQ-024 不记录登录名和账号 ID（操作者显示为未记录名）。"操作者显示登录名、'玩家'标记和账号 ID"不适用于这一条，建议注明，免得误判。

### 第 2 轮（task-rework）

按协调方要求处理 Review R1 的 F-1、N-2，并补写 UDP 占用说明（对应 N-1）。本轮只改了 `Document/tasks/player-self-service-portal/用户测试流程.md` 和本文件，没有启动或结束任何进程，没有 commit，没有使用 checkout / restore / stash。

- **F-1（第 11 步）**：
  - 删掉了"（或刷新）"，操作改为"在已打开的玩家页面上切换一次页面，或提交一次操作"，预期仍是进入"玩家后台已关闭"页。
  - 补了一句：此时刷新页面会回到登录页，并且没有"玩家"选项，这同样是正确行为。
  - 依据是 `Portal.tsx` 的 `probe()`：刷新时先读 `auth/status`，读到 `enabled=false` 就显示不带"玩家"选项的登录页。主控的浏览器实测结果与此一致。
- **N-2（第 13 步）**：在"操作者显示登录名、'玩家'标记和账号 ID"后面注明例外：第 3 步用不存在的账号登录失败的那条审计，不记录登录名和账号 ID。
- **UDP 占用**：两处写明 UDP 12311/12312 目前被另一个会话的 41xxx 测试服占用，用户测试服要等它停掉才能启动，就绪状态由主控填写。
  - 测试服表格的"就绪情况"一行；
  - "前置条件"第一条。
- **检查**：
  - 文档 CR 为 0；
  - 用户副本主文件的 SHA-256 前缀仍为 `6fba1b59`；
  - 其它文件与第 1 轮相同。

### Independent Quick Review 2

- 审查对象：第 2 轮（task-rework）后的未提交改动：`Document/tasks/player-self-service-portal/用户测试流程.md` 与本文件的第 2 轮记录。
  - `DailyRefill.test.tsx` 的 diff 与 R1 时相同（12 增 9 删），脚本与库副本没有变化。
  - `verification/` 由主控放置，不在本次审查范围内。
- 适用标准：同 R1（Task Unit + Deliverable），只复核 R1 的未决项以及修改可能带来的回归。
- 只读核对：没有启停进程，没有读取凭据文件，没有打开 agent 副本。

```yaml
kind: rks-review
verdict: approved
```

#### 复核

- **F-1 已解决。** 第 11 步改为"在已打开的玩家页面上切换一次页面，或提交一次操作"，并补了"刷新后回到没有玩家选项的登录页，同样正确"。
  - 与 `Portal.tsx` 的 `probe()` 一致（刷新时读到 `enabled=false`，进入没有玩家选项的登录页）。
  - 与 `PlayerApp.tsx` 的 `call` 一致（`portal_closed` 时调用 `leave('onClosed')`）。
- **N-2 已解决。** 第 13 步注明了不存在账号这一例外，与 `PlayerPortalAudit.RejectedLogin` 一致：未知账号不记录登录名和账号 ID。
- **N-1 已写明。** 测试服表格的"就绪情况"和前置条件第一条都写了 UDP 12311/12312 被另一个会话占用。是否启动由主控决定，这项不属于本单元的缺陷。
- **回归**：
  - 其余步骤没有变化；
  - 文档 CR 为 0；
  - 用户副本主文件 SHA-256 前缀仍为 `6fba1b59`，`-wal` 为 0 字节；
  - R1 段落未被改动。

#### 非阻塞提醒

- **N-3：第 11 步的"切换一次页面"可以再具体一点。**
  - 进入「记录」页会立刻请求 `operations`，能马上触发已关闭页。
  - 「我的账号」切换时不发请求，余额靠外壳每 60 秒刷新一次 `me`。「发邮件」页的物品搜索没有输入时不检索。所以切到这两页时，可能要等最多约 60 秒才进入已关闭页。
  - 建议写成"切到「记录」页"。不改也不会得出错误结论：提交操作或等待自动刷新，同样会进入已关闭页。
